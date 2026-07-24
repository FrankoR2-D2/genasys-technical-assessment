# Service Method Flowcharts

Control-flow detail for the methods that do more than a CRUD pass-through.
These are the methods worth understanding branch-by-branch; simple
list/get/create/update/delete methods on `ProductService`/`CustomerService`
aren't diagrammed here — they're straightforward EF Core CRUD with
validation and (for Product) a cache read-through.

## `OrderService.CreateAsync`

The one method that justifies this whole documentation set — every error
scenario in the spec funnels through here.

```mermaid
flowchart TD
    Start(["CreateAsync(request, idempotencyKey, token)"]) --> Idem{"idempotencyKey<br/>provided?"}
    Idem -- yes --> IdemLookup["Orders.Where(IdempotencyKey == key)<br/>.SingleOrDefaultAsync"]
    IdemLookup --> IdemFound{"existing order?"}
    IdemFound -- yes --> Replay["log replay, GetByIdAsync(existingId)"]
    Replay --> Return201A(["return existing OrderResponse<br/>(201, same order)"])
    IdemFound -- no --> LoadCustomer
    Idem -- no --> LoadCustomer

    LoadCustomer["Customers.SingleOrDefaultAsync(CustomerId)"] --> CustFound{"found?"}
    CustFound -- no --> NF1["throw NotFoundException"]
    NF1 --> Status404A(["404"])
    CustFound -- yes --> LoadProducts["Products.Where(id IN productIds)<br/>.ToDictionaryAsync"]

    LoadProducts --> MissingCheck{"any requested<br/>productId missing?"}
    MissingCheck -- yes --> NF2["throw NotFoundException<br/>(unknown product(s))"]
    NF2 --> Status404B(["404"])
    MissingCheck -- no --> BuildItems["build OrderItem[] —<br/>price/name snapshotted from catalog,<br/>never from the request body"]

    BuildItems --> AvailLoop["for each item:<br/>inventoryClient.GetAsync(productId)"]
    AvailLoop --> AvailCheck{"AvailableQuantity<br/>&lt; requested, any item?"}
    AvailCheck -- yes --> II["throw InsufficientInventoryException<br/>(before reserving anything)"]
    II --> Status409A(["409"])
    AvailCheck -- no --> ReserveLoop

    ReserveLoop["try: for each item<br/>inventoryClient.ReserveAsync(...)<br/>track in reserved[]"] --> ReserveOutcome{"outcome"}
    ReserveOutcome -- "DomainException<br/>(lost a race — 409 from Inventory)" --> RelA["ReleaseAllAsync(reserved, CancellationToken.None)"]
    RelA --> RethrowA["rethrow original DomainException"]
    RethrowA --> Status409B(["409 (or whatever Inventory returned)"])
    ReserveOutcome -- "other Exception<br/>(Inventory unreachable)" --> RelB["log error<br/>ReleaseAllAsync(reserved, CancellationToken.None)"]
    RelB --> USE1["throw UpstreamServiceUnavailableException"]
    USE1 --> Status503A(["503"])
    ReserveOutcome -- "all reserved OK" --> SaveOrder

    SaveOrder["Orders.Add(order, Status=Pending)<br/>OrderStatusHistories.Add(null→Pending)<br/>SaveChangesAsync"] --> Pay["try: paymentClient.ProcessAsync(...)"]

    Pay --> PayOutcome{"outcome"}
    PayOutcome -- "Exception<br/>(Payment unreachable/timeout)" --> RelC["log error<br/>ReleaseAllAsync(items, CancellationToken.None)<br/>TransitionAsync(Cancelled, CancellationToken.None)"]
    RelC --> USE2["throw UpstreamServiceUnavailableException"]
    USE2 --> Status503B(["503, order left Cancelled"])
    PayOutcome -- "PaymentStatus.Completed" --> Consume["inventoryService.ConsumeReservationsAsync(orderId)<br/>(in-process, not HTTP)<br/>TransitionAsync(Confirmed)"]
    Consume --> Return201B(["return OrderResponse<br/>201, Confirmed"])
    PayOutcome -- "PaymentStatus.Failed" --> RelD["ReleaseAllAsync(items, CancellationToken.None)<br/>TransitionAsync(Cancelled, CancellationToken.None)"]
    RelD --> PF["throw PaymentFailedException"]
    PF --> Status402(["402, order left Cancelled"])
```

Three details this diagram makes explicit that are easy to miss reading the
code top-to-bottom:

1. **Two different exception-handling shapes in the same method** — the
   reservation loop distinguishes `DomainException` (a real business
   rejection, like losing a race for the last unit — rethrown as-is) from
   any other exception (treated as "Inventory is unreachable" and mapped to
   `503`). The payment call doesn't make that distinction because
   `IPaymentApiClient.ProcessAsync` never throws a domain exception for a
   *declined* payment — a decline is a normal `200` response with
   `Status: "Failed"`, handled in the success branch. Only a genuinely
   broken payment service throws.
2. **Every compensation call uses `CancellationToken.None`, not the
   request's token.** If the original request is cancelled partway through,
   the code must still finish releasing inventory and marking the order
   `Cancelled` — using the (now-cancelled) request token there would let the
   cleanup itself get cancelled, stranding held stock against a `Pending`
   order forever. Full writeup: [resilience-and-consistency.md](../resilience-and-consistency.md#cancellation-can-prevent-compensation).
3. **The availability check and the reservation loop are two separate
   passes.** Checking `GET /inventory/{id}` for every item *before*
   reserving any of them means a doomed order (item 3 of 3 is out of stock)
   never partially reserves items 1 and 2 in the first place — the
   reservation loop only exists to handle the race where availability
   changed *between* the check and the reserve call.

## `InventoryService.ReserveAsync`

```mermaid
flowchart TD
    Start(["ReserveAsync(productId, request, token)"]) --> Lock["KeyedLockProvider.AcquireAsync(productId)<br/>— serializes this productId only"]
    Lock --> Load["InventoryItems.SingleOrDefaultAsync(productId)"]
    Load --> Found{"found?"}
    Found -- no --> NF["throw NotFoundException"]
    NF --> S404(["404"])
    Found -- yes --> Check{"AvailableQuantity<br/>&lt; request.Quantity?"}
    Check -- yes --> II["throw InsufficientInventoryException"]
    II --> S409(["409"])
    Check -- no --> Debit["AvailableQuantity -= qty<br/>ReservedQuantity += qty<br/>Reservations.Add(Active, ExpiresAt = now+15min)"]
    Debit --> Save["SaveChangesAsync"]
    Save --> Release["lock released (await using)"]
    Release --> Return(["return InventoryItemResponse"])
```

The lock is what makes the "two orders race for the last unit" scenario
safe: both requests call `ReserveAsync` for the same `productId`, but the
second one blocks on `AcquireAsync` until the first has committed its debit
— so the second sees the *updated* `AvailableQuantity`, not a stale read.
Proven by `InventoryServiceTests`' concurrency test (two simultaneous
reservations, only one succeeds).

## `InventoryService.ReleaseAsync`

```mermaid
flowchart TD
    Start(["ReleaseAsync(productId, request, token)"]) --> Lock["KeyedLockProvider.AcquireAsync(productId)"]
    Lock --> LoadItem["InventoryItems.SingleOrDefaultAsync(productId)"]
    LoadItem --> ItemFound{"found?"}
    ItemFound -- no --> NF1["throw NotFoundException"]
    NF1 --> S404A(["404"])
    ItemFound -- yes --> LoadRes["Reservations.Where(productId, orderId, Status=Active)<br/>.OrderBy(ReservedAt).FirstOrDefaultAsync"]
    LoadRes --> ResFound{"found?"}
    ResFound -- no --> NF2["throw NotFoundException<br/>(no active reservation)"]
    NF2 --> S404B(["404"])
    ResFound -- yes --> QtyCheck{"reservation.Quantity<br/>== request.Quantity?"}
    QtyCheck -- no --> CE["throw ConflictException"]
    CE --> S409(["409"])
    QtyCheck -- yes --> Credit["reservation.Status = Released<br/>AvailableQuantity += qty<br/>ReservedQuantity -= qty"]
    Credit --> Save["SaveChangesAsync"]
    Save --> Return(["return InventoryItemResponse"])
```

Release targets a *specific reservation row* (matched on `productId` +
`orderId`, earliest first), not a bare decrement — releasing order A's hold
can never accidentally touch order B's, even if both reserved the same
product.

## `PaymentService.ProcessAsync`

```mermaid
flowchart TD
    Start(["ProcessAsync(request, idempotencyKey, token)"]) --> Idem{"idempotencyKey<br/>provided?"}
    Idem -- yes --> Lookup["PaymentTransactions.Where(IdempotencyKey == key)<br/>.SingleOrDefaultAsync"]
    Lookup --> Found{"existing?"}
    Found -- yes --> Replay(["return existing PaymentTransactionResponse<br/>(no new charge)"])
    Found -- no --> Build
    Idem -- no --> Build

    Build["build PaymentTransaction<br/>MaskedReference = mask(instrumentRef)"] --> Sim{"instrumentRef ==<br/>'DECLINE' (case-insensitive)?"}
    Sim -- yes --> Failed["Status = Failed"]
    Sim -- no --> Completed["Status = Completed"]
    Failed --> Save["PaymentTransactions.Add<br/>SaveChangesAsync"]
    Completed --> Save
    Save --> Return(["return PaymentTransactionResponse<br/>(200 either way — decline is not an HTTP error)"])
```

The `DECLINE` sentinel is the entire "payment gateway" — there's no real
processor. `MaskedReference` (`**** 4242` shape) is stored instead of
`InstrumentReference`; the raw value is never persisted.

## `AuthService.AuthenticateAsync`

```mermaid
flowchart TD
    Start(["AuthenticateAsync(username, password, token)"]) --> Load["Users.SingleOrDefaultAsync(u => u.Username == username)"]
    Load --> Verify{"user found AND<br/>BCrypt.Verify(password, hash)?"}
    Verify -- no --> Warn["log warning: failed login"]
    Warn --> Null(["return null → AuthController maps to 401"])
    Verify -- yes --> Claims["claims: sub=UserId, name, role, jti=new Guid"]
    Claims --> Sign["sign JwtSecurityToken<br/>HS256, JwtOptions.Key, Issuer, Audience, expiry"]
    Sign --> Return(["return TokenResponse(accessToken, 'Bearer', expiresIn)"])
```
