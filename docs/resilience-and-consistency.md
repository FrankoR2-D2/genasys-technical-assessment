# Resilience & Consistency

The order creation flow spans three services and a real (if loopback) HTTP
hop between them, which means it inherits the classic distributed-systems
problems even though everything happens to run in one process today:
requests get retried, requests get cancelled, and two requests can race
each other. This doc walks through each concern as a concrete use case —
what goes wrong without the guard, what guard exists, and where the
remaining gaps are.

## Idempotency

**Use case:** a client calls `POST /api/orders`, the order is created and
payment succeeds, but the response is lost on the way back (client
timeout, proxy hiccup, whatever). The client — following the spec's own
guidance to handle "concurrent order processing" and general HTTP retry
hygiene — retries the exact same request.

Without protection, this creates a second order, reserves inventory a
second time, and charges the customer twice. The fix is an
`Idempotency-Key` header the client generates once per logical operation
and replays verbatim on retry:

- `OrderService.CreateAsync` — checks
  `Orders.Where(o => o.IdempotencyKey == idempotencyKey).SingleOrDefaultAsync`
  before doing anything else. A match short-circuits straight to
  `GetByIdAsync(existingId)` — no new reservation, no new payment call, same
  `OrderResponse` returned as the first time. ([OrderService.cs:65-77](../src/Genasys.Api/Services/OrderService.cs))
- `PaymentService.ProcessAsync` — same pattern, keyed on
  `PaymentTransaction.IdempotencyKey`. This matters independently of the
  order-level key: `OrderService` forwards its own `idempotencyKey` to the
  payment call, so a retried *order* also produces a replayed *payment*,
  not a second charge. ([PaymentService.cs:51-61](../src/Genasys.Api/Services/PaymentService.cs))

**The gap this closes vs. the gap it doesn't:** a plain (non-unique) index
on `IdempotencyKey` only speeds up the lookup — it does nothing to stop two
concurrent requests carrying the same key from both passing the
"does this exist?" check before either has saved, and both creating a row.
Under EF Core InMemory (and any real database), that's a genuine race:
thread A reads "no existing order", thread B reads "no existing order",
both proceed to create one. The index is now `.IsUnique()` on both `Order`
and `PaymentTransaction` ([OrderConfiguration.cs:14](../src/Genasys.Api/Data/Configurations/OrderConfiguration.cs),
[PaymentTransactionConfiguration.cs:13](../src/Genasys.Api/Data/Configurations/PaymentTransactionConfiguration.cs))
so the loser of that race now gets a `DbUpdateException` from the second
`SaveChangesAsync` instead of silently succeeding — a real constraint
violation is a better failure mode than a silent duplicate, even though the
loser currently surfaces as an unhandled `500` rather than a friendly
"here's the order the other request created" response. Closing that last
mile (catching the constraint violation and re-running the replay lookup)
is a reasonable next step, not yet implemented.

**What a unique index does *not* protect against:** reusing the same key
with a *different* payload (different items, different amount). Right now
that would silently return the *first* request's result, ignoring the
second payload entirely — there's no check that the replayed request
matches what was stored. A stricter implementation would hash the request
body alongside the key and reject a mismatch with `422` instead of silently
serving the old result.

## Retries can repeat non-idempotent operations

**Use case:** `OrderService` calls `IInventoryApiClient.ReserveAsync` for a
line item. The reservation succeeds on the Inventory side — stock is
debited, an `InventoryReservation(Active)` row is written — but the TCP
connection drops before the `200 OK` makes it back to the caller. Polly, configured
as `AddTransientHttpErrorPolicy(policy => policy.WaitAndRetryAsync(3, ...))`
on both typed clients ([WebApplicationBuilderExtensions.cs:118](../src/Genasys.Api/Configuration/WebApplicationBuilderExtensions.cs)),
sees what looks like a transient failure and retries the same `POST
/api/inventory/{productId}/reserve` — automatically, with no visibility
into whether the first attempt actually landed.

**What happens today:** `InventoryService.ReserveAsync` has no way to tell
"this is a retry of a reservation I already made" from "this is a genuinely
new reservation request" — it unconditionally debits `AvailableQuantity`
and inserts a new `InventoryReservation` row every time it's called
([InventoryService.cs:52-83](../src/Genasys.Api/Services/InventoryService.cs)).
A retried reserve call double-reserves: two `Active` reservations for the
same `(productId, orderId)`, twice the stock debited for one order.
Contrast with `ReleaseAsync`, which already looks up a specific reservation
row by `(productId, orderId, Status == Active)` before acting on it — the
same lookup-before-act shape would close this gap on `ReserveAsync` too
(check for an existing `Active` reservation for this `(productId, orderId)`
pair first, return it unchanged if found, only create a new row otherwise).
Not yet implemented — flagged in the completion status as a known,
documented gap rather than an unknown one.

**Why the retry exists at all despite the risk:** it's not free lunch, it's
a genuine tradeoff. Without retries, any single dropped packet between two
in-process "services" (loopback though the hop is) turns into a hard
`503` and a cancelled order — the spec explicitly calls out "service
unavailable" as a scenario to handle gracefully, and blind failure on the
first transient blip is arguably worse UX than a bounded retry. The right
long-term fix isn't removing the retry, it's making the retried operation
safe to repeat (the dedup check above) rather than just tolerant of
failure.

**Note:** `PaymentApiClient.ProcessAsync` doesn't have this problem *for
its own retries*, because `OrderService` already passes an
`Idempotency-Key` through to it — a Polly-retried payment call is itself an
idempotent replay, protected by the mechanism above. The risk is
specifically on the `Reserve` call, which carries no equivalent key.

## Cancellation can prevent compensation

**Use case:** a client calls `POST /api/orders`, ASP.NET Core wires the
request's `CancellationToken` to `HttpContext.RequestAborted`, and the
client disconnects (browser tab closed, client-side timeout, load balancer
idle timeout) *after* inventory has been reserved but *before* the payment
call returns. `OrderService.CreateAsync`'s `catch` block around the payment
call reacts to this exactly like any other failure: it releases the
reservations and transitions the order to `Cancelled`.

**The bug this used to have:** every compensation call in that `catch`
block — `ReleaseAllAsync(...)` and `TransitionAsync(..., Cancelled, ...)` —
used to be passed the *same* `cancellationToken` as the original request.
Once the client disconnects, that token is already cancelled. The next
`await` inside `ReleaseAllAsync`/`TransitionAsync` (an EF Core
`SaveChangesAsync`) throws `OperationCanceledException` immediately instead
of doing the release/transition — so the order is left `Pending` forever,
with inventory still marked `Reserved`, and nothing ever cleans it up. From
the outside this looks like "inventory silently vanished" — it isn't sold,
isn't available, isn't attached to any order that will ever complete.

**The fix:** compensation calls in both `catch` blocks of
`OrderService.CreateAsync` now pass `CancellationToken.None` instead of the
inbound token ([OrderService.cs](../src/Genasys.Api/Services/OrderService.cs) —
see the `ReleaseAllAsync`/`TransitionAsync` calls in the reservation and
payment `catch` blocks, and in the payment-declined path). The reasoning:
compensation isn't optional just because nobody's waiting for the HTTP
response anymore — releasing held stock and recording *why* an order died
needs to complete regardless of whether the original caller stuck around to
see the result. `CancellationToken.None` is the standard pattern for "this
cleanup must run to completion" in .NET; a more elaborate version would use
a short-lived linked token (e.g. a 5-second timeout) instead of `None`, so
a *truly* hung downstream call can't block cleanup forever either — not
implemented here since Polly's own per-call timeout already bounds how
long the compensation calls themselves can hang.

**Why this matters more than it might look:** this exact bug class — using
a request-scoped cancellation token inside "finally-shaped" cleanup code —
is a common source of production incidents that only show up under load or
flaky networks, precisely because it's invisible in the happy path and in
most manual testing (nobody manually cancels a curl request mid-flight).
It's exactly the kind of thing a concurrency/timeout-focused code review
catches and a spec-compliance check doesn't.

## Concurrency, for context

The design decision behind this is covered in [architecture.md](architecture.md); this section is just the use-case framing.

Two independent guards, defending against two different races:

- **`KeyedLockProvider`** (`Common/KeyedLockProvider.cs`) — an in-process
  `ConcurrentDictionary<string, SemaphoreSlim>` keyed by `productId`,
  serializing the read-check-write critical section inside
  `ReserveAsync`/`ReleaseAsync`/`ConsumeReservationsAsync`. This is what
  actually stops two simultaneous orders for the same product from both
  reading `AvailableQuantity = 1` and both deciding they can proceed —
  proven by a real concurrency test in `InventoryServiceTests`.
- **`RowVersion` optimistic concurrency** — a `Guid` on `Order` and
  `InventoryItem`, auto-bumped by `AppDbContext.SaveChanges`/
  `SaveChangesAsync` overrides (`BumpRowVersions()`) on every tracked
  `IHasRowVersion` entity before it's persisted. This is the second line of
  defense — any writer that bypasses the keyed lock (there currently isn't
  one, but future code might add a path that does) still can't silently
  overwrite a concurrently-modified row; EF Core throws
  `DbUpdateConcurrencyException`, mapped by `GlobalExceptionHandler` to a
  clean `409`.

## Summary — what's guaranteed today vs. not

| Guarantee | Status |
|---|---|
| Retried order creation doesn't duplicate the order | ✅ unique `IdempotencyKey` index + replay lookup |
| Retried payment doesn't double-charge | ✅ unique `IdempotencyKey` index + replay lookup |
| Concurrent identical idempotency-key requests can't both create a row | ✅ (DB constraint) but surfaces as `500`, not a clean replay, for the loser |
| Reused idempotency key with a *different* payload is rejected | ❌ not implemented — silently serves the original result |
| Retried inventory reservation doesn't double-reserve | ❌ known gap — no dedup check on `ReserveAsync` |
| Client cancellation during compensation doesn't strand held inventory | ✅ fixed — compensation runs on `CancellationToken.None` |
| Two orders can't oversell the same product | ✅ `KeyedLockProvider` + tested |
| Concurrent writes to the same `Order`/`InventoryItem` are detected | ✅ `RowVersion`, auto-bumped, mapped to `409` |
