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
The natural instinct is to reach for a **unique** index instead, and both
`Order` and `PaymentTransaction` do have one
([OrderConfiguration.cs:14](../src/Genasys.Api/Data/Configurations/OrderConfiguration.cs),
[PaymentTransactionConfiguration.cs:13](../src/Genasys.Api/Data/Configurations/PaymentTransactionConfiguration.cs)) —
but it turns out **EF Core's InMemory provider doesn't enforce unique
indexes at all**. This was verified directly: two separate `DbContext`
instances inserting the same key, even fully sequentially with no race
involved, both succeed with no exception. So on this provider the unique
index is a correct piece of schema documentation and *would* work on a
real database (SQL Server, Postgres, etc.), but it provides zero actual
protection here.

The real guarantee comes from **`KeyedLockProvider`** — the same
per-key async lock already used for inventory concurrency (see
[Concurrency](#concurrency-for-context) below), reused with a different key
namespace. `OrderService.CreateAsync` and `PaymentService.ProcessAsync` both
acquire a lock keyed on `"order-idem:{key}"` / `"payment-idem:{key}"`
*before* doing the check-then-insert, and hold it for the entire operation —
reservation, payment, everything. A concurrent duplicate request simply
queues behind the lock instead of racing; by the time it acquires the lock,
the winner has already committed, so its own "does this exist?" check finds
the row and returns the replay. The `DbUpdateException` catch around each
`SaveChangesAsync` still exists as a second line of defense (harmless on
InMemory since it never fires there, but a real backstop against the unique
constraint on a production database), rather than removed — belt-and-suspenders
costs nothing here.

**What neither the lock nor a unique index protects against:** reusing the
same key with a *different* payload (different items, different amount).
Right now that would silently return the *first* request's result, ignoring
the second payload entirely — there's no check that the replayed request
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

**The fix:** `InventoryService.ReserveAsync` now checks for an existing
`Active` reservation for this exact `(productId, orderId)` pair *before*
debiting anything ([InventoryService.cs:69-86](../src/Genasys.Api/Services/InventoryService.cs)) —
the same lookup-before-act shape `ReleaseAsync` already used. If found with
a matching quantity, it's a retry of a reservation that already landed:
return the current state unchanged, no second debit. If found with a
*different* quantity, that's a genuine conflict (not a clean retry — the
caller is asking for something different under the same order), rejected
with `409` rather than silently overwritten.

**Why the retry exists at all despite the risk:** it's not free lunch, it's
a genuine tradeoff. Without retries, any single dropped packet between two
in-process "services" (loopback though the hop is) turns into a hard
`503` and a cancelled order — the spec explicitly calls out "service
unavailable" as a scenario to handle gracefully, and blind failure on the
first transient blip is arguably worse UX than a bounded retry. The right
long-term fix isn't removing the retry, it's making the retried operation
safe to repeat (the dedup check above), which is what closes the gap here.

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

**A subtler bug the lock alone didn't catch:** writing the integration test
for the idempotency race (above) surfaced a real concurrency bug that had
nothing to do with idempotency at all. `InventoryService.GetByProductIdAsync`
— called by `OrderService.CreateAsync`'s pre-reservation availability check,
*before* the keyed lock is acquired — used a tracked EF Core query. That
attaches a snapshot of `InventoryItem` to the request's `DbContext` early.
When `ReserveAsync` later queried the same entity *inside* the lock, EF
Core's identity map returned that same already-tracked (and by then stale)
instance instead of re-reading the store — silently defeating the lock's
entire purpose of guaranteeing a fresh read inside the critical section.
The same issue existed in `ConsumeReservationsAsync`, whichever call
happened to touch a given `InventoryItem` second within one request would
read a cached snapshot rather than the latest committed state. Fixed by
making `GetByProductIdAsync`/`ListAsync` use `.AsNoTracking()` (they're
read-only, so nothing should be tracking them in the first place) and by
having every lock-protected mutation (`ReserveAsync`, `ReleaseAsync`,
`ConsumeReservationsAsync`) explicitly `ReloadAsync()` the entity after
loading it, so a read inside a critical section is always a real read,
never a cached one — regardless of what else touched that entity earlier
in the same `DbContext`'s lifetime. This is the kind of bug that's
essentially undetectable by code review alone; it only reproduces under
genuine concurrency across separate `DbContext` instances, which is exactly
why the concurrent-idempotency integration test (not a unit test against a
single shared context) is what caught it.

## Summary — what's guaranteed today vs. not

| Guarantee | Status |
|---|---|
| Retried order creation doesn't duplicate the order | ✅ `KeyedLockProvider` (`order-idem:{key}`) + replay lookup — tested under real concurrency |
| Retried payment doesn't double-charge | ✅ `KeyedLockProvider` (`payment-idem:{key}`) + replay lookup |
| Concurrent identical idempotency-key requests can't both create a row | ✅ the lock, not the unique index — EF Core's InMemory provider doesn't enforce unique indexes at all (verified) |
| Reused idempotency key with a *different* payload is rejected | ❌ not implemented — silently serves the original result |
| Retried inventory reservation doesn't double-reserve | ✅ fixed — `ReserveAsync` checks for an existing `Active` reservation on `(productId, orderId)` first |
| Client cancellation during compensation doesn't strand held inventory | ✅ fixed — compensation runs on `CancellationToken.None` |
| Two orders can't oversell the same product | ✅ `KeyedLockProvider` + tested, including via a real HTTP-level concurrency test |
| Concurrent writes to the same `Order`/`InventoryItem` are detected | ✅ `RowVersion`, auto-bumped, mapped to `409` — but only once reads inside a lock are guaranteed fresh (`AsNoTracking`/`ReloadAsync`, see above) |
