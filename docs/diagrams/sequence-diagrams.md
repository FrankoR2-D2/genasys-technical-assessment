# Sequence Diagrams

Runtime interactions between the classes in
[class-diagram.md](class-diagram.md), using their real names so this maps
1:1 onto the source. All diagrams assume a valid bearer token unless the
scenario is specifically about auth.

## 1. Auth — token issuance

```mermaid
sequenceDiagram
    participant Client
    participant AC as AuthController
    participant AS as AuthService
    participant DB as AppDbContext

    Client->>AC: POST /api/auth/token {username, password}
    AC->>AS: AuthenticateAsync(username, password)
    AS->>DB: Users.SingleOrDefaultAsync(u => u.Username == username)
    DB-->>AS: User | null
    alt user missing or BCrypt.Verify fails
        AS-->>AC: null
        AC-->>Client: 401 Unauthorized (ProblemDetails)
    else credentials valid
        AS->>AS: build claims (sub, name, role, jti)
        AS->>AS: sign JwtSecurityToken (HS256, JwtOptions.Key)
        AS-->>AC: TokenResponse(accessToken, "Bearer", expiresIn)
        AC-->>Client: 200 OK {accessToken, tokenType, expiresIn}
    end
```

Every endpoint except this one requires the resulting bearer token — see
[auth-and-security.md](../auth-and-security.md) for how the token is
verified on the way back in.

## 2. Order creation — happy path

The full success path, showing the real HTTP hops between
`OrderController` and `InventoryController`/`PaymentsController` (loopback,
but not in-process calls — see [architecture.md](../architecture.md#layered-request-flow)).

```mermaid
sequenceDiagram
    participant Client
    participant OC as OrdersController
    participant VF as ValidationFilter
    participant OS as OrderService
    participant IAC as IInventoryApiClient
    participant IC as InventoryController
    participant ISv as InventoryService
    participant PAC as IPaymentApiClient
    participant PC as PaymentsController
    participant PSv as PaymentService
    participant DB as AppDbContext

    Client->>OC: POST /api/orders (Idempotency-Key?)
    OC->>VF: CreateOrderRequest
    VF->>VF: CreateOrderRequestValidator.ValidateAsync
    VF-->>OC: valid, continue
    OC->>OS: CreateAsync(request, idempotencyKey)

    OS->>DB: Orders.Where(IdempotencyKey == key) — no match
    OS->>DB: Customers.SingleOrDefaultAsync(id)
    OS->>DB: Products.Where(id IN productIds).ToDictionaryAsync
    OS->>OS: build OrderItem[] with catalog price (never client-supplied)

    loop each line item — availability check
        OS->>IAC: GetAsync(productId)
        IAC->>IC: GET /api/inventory/{productId}
        IC->>ISv: GetByProductIdAsync(productId)
        ISv->>DB: InventoryItems.SingleOrDefaultAsync
        DB-->>ISv: InventoryItem
        ISv-->>IC: InventoryItemResponse
        IC-->>IAC: 200 OK
        IAC-->>OS: InventoryItemResponse
    end
    Note over OS: all items available — proceed

    loop each line item — reserve
        OS->>IAC: ReserveAsync(productId, {orderId, quantity})
        IAC->>IC: POST /api/inventory/{productId}/reserve
        IC->>ISv: ReserveAsync(productId, request)
        ISv->>ISv: KeyedLockProvider.AcquireAsync(productId)
        ISv->>DB: re-check AvailableQuantity, debit, add InventoryReservation(Active)
        ISv->>DB: SaveChangesAsync
        ISv-->>IC: InventoryItemResponse
        IC-->>IAC: 200 OK
        IAC-->>OS: InventoryItemResponse
    end

    OS->>DB: Orders.Add(order, Status=Pending) + OrderStatusHistories.Add(null→Pending)
    OS->>DB: SaveChangesAsync

    OS->>PAC: ProcessAsync({orderId, amount, method, instrumentRef}, idempotencyKey)
    PAC->>PC: POST /api/payments/process (Idempotency-Key header)
    PC->>PSv: ProcessAsync(request, idempotencyKey)
    PSv->>DB: PaymentTransactions.Where(IdempotencyKey == key) — no match
    PSv->>PSv: simulate gateway — instrumentRef != "DECLINE" → Completed
    PSv->>DB: PaymentTransactions.Add(transaction, Status=Completed)
    PSv->>DB: SaveChangesAsync
    PSv-->>PC: PaymentTransactionResponse(Status="Completed")
    PC-->>PAC: 200 OK
    PAC-->>OS: PaymentTransactionResponse

    OS->>ISv: ConsumeReservationsAsync(orderId)  note: in-process, not HTTP
    ISv->>DB: reservations Active→Consumed, TotalQuantity -= qty
    OS->>DB: Order.Status = Confirmed, OrderStatusHistories.Add(Pending→Confirmed)
    OS-->>OC: OrderResponse(Status="Confirmed")
    OC-->>Client: 201 Created
```

## 3. Order creation — payment declined

Diverges from step "loop each line item — reserve" onward in diagram 2.

```mermaid
sequenceDiagram
    participant OS as OrderService
    participant PAC as IPaymentApiClient
    participant PC as PaymentsController
    participant PSv as PaymentService
    participant IAC as IInventoryApiClient
    participant DB as AppDbContext

    Note over OS: inventory reserved, Order saved as Pending (as in diagram 2)
    OS->>PAC: ProcessAsync({..., instrumentRef: "DECLINE"}, idempotencyKey)
    PAC->>PC: POST /api/payments/process
    PC->>PSv: ProcessAsync(request, idempotencyKey)
    PSv->>PSv: instrumentRef == "DECLINE" → Failed
    PSv->>DB: PaymentTransactions.Add(transaction, Status=Failed)
    PSv-->>PC: PaymentTransactionResponse(Status="Failed")
    PC-->>PAC: 200 OK (payment call itself succeeded — the *charge* failed)
    PAC-->>OS: PaymentTransactionResponse(Status="Failed")

    OS->>OS: Enum.Parse(payment.Status) != Completed
    loop each reserved item
        OS->>IAC: ReleaseAsync(productId, {orderId, quantity})
        Note over OS,IAC: uses CancellationToken.None — must complete<br/>even if the inbound request was cancelled
    end
    OS->>DB: Order.Status = Cancelled, OrderStatusHistories.Add(Pending→Cancelled, "Payment declined.")
    OS-->>OS: throw PaymentFailedException
    Note over OS: caught by GlobalExceptionHandler → 402 Payment Required
```

## 4. Order creation — inventory/payment service unavailable

Two independent failure points collapse to the same shape: an exception
from the typed `HttpClient` call (after Polly's 3 retries are exhausted)
triggers compensation and a `503`.

```mermaid
sequenceDiagram
    participant OS as OrderService
    participant PAC as IPaymentApiClient
    participant Polly
    participant IAC as IInventoryApiClient

    OS->>PAC: ProcessAsync(...)
    PAC->>Polly: HTTP POST (transient fault / timeout)
    Polly->>Polly: retry x3 (200ms, 400ms, 600ms backoff)
    Polly-->>PAC: still failing
    PAC-->>OS: throws (network exception or non-2xx wrapped as UpstreamServiceUnavailableException)

    OS->>OS: catch (Exception ex)
    loop each reserved item
        OS->>IAC: ReleaseAsync(productId, ..., CancellationToken.None)
    end
    OS->>OS: TransitionAsync(order, Cancelled, "Payment service unavailable.", CancellationToken.None)
    OS-->>OS: throw UpstreamServiceUnavailableException
    Note over OS: caught by GlobalExceptionHandler → 503 Service Unavailable
```

The same shape applies if the *reservation* call itself throws (Inventory
service down before payment is ever attempted) — see
`OrderService.CreateAsync`'s first `try/catch` block, covered in
[flowcharts.md](flowcharts.md#orderservicecreateasync).

## 5. Idempotent replay

```mermaid
sequenceDiagram
    participant Client
    participant OC as OrdersController
    participant OS as OrderService
    participant DB as AppDbContext

    Note over Client,DB: First request already completed successfully (diagram 2)
    Client->>OC: POST /api/orders (same Idempotency-Key, retried after a dropped response)
    OC->>OS: CreateAsync(request, idempotencyKey)
    OS->>DB: Orders.Where(o => o.IdempotencyKey == idempotencyKey).Select(Id).SingleOrDefaultAsync
    DB-->>OS: existing Order.Id
    OS->>OS: log "Idempotent replay of order creation"
    OS->>DB: GetByIdAsync(existingId) — no new reservation, no new charge
    OS-->>OC: OrderResponse (same order as before)
    OC-->>Client: 201 Created (original order, not a duplicate)
```

Without protection, two concurrent requests carrying the same key could
both pass the `SingleOrDefaultAsync` check above before either had saved,
and both create an order. `CreateAsync` guards against this by acquiring a
`KeyedLockProvider` lock scoped to the idempotency key *before* running this
whole sequence — not shown in the diagram above for clarity, since it wraps
the entire operation rather than a single step. See
[resilience-and-consistency.md](../resilience-and-consistency.md#idempotency)
for the full writeup, including why a unique database index alone
turned out not to be enough here.
