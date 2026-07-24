# UML Class Diagrams

Two views: the persisted domain model (`Entities/`), and the
service/controller layer that operates on it. Diagram source is Mermaid —
renders directly on GitHub.

## Domain model

```mermaid
classDiagram
    class Customer {
        +Guid Id
        +string Name
        +string Email
        +Address ShippingAddress
        +Address BillingAddress
        +bool IsDeleted
        +DateTime CreatedAt
        +DateTime UpdatedAt
    }

    class ProductCategory {
        +Guid Id
        +string Name
        +Guid? ParentCategoryId
    }

    class Product {
        +string ProductId
        +string Name
        +string Sku
        +string? Description
        +decimal UnitPrice
        +Guid? CategoryId
        +bool IsDeleted
        +DateTime CreatedAt
        +DateTime UpdatedAt
    }

    class InventoryItem {
        <<IHasRowVersion>>
        +string ProductId
        +int TotalQuantity
        +int AvailableQuantity
        +int ReservedQuantity
        +Guid RowVersion
    }

    class InventoryReservation {
        +Guid Id
        +string ProductId
        +Guid OrderId
        +int Quantity
        +ReservationStatus Status
        +DateTime ReservedAt
        +DateTime? ExpiresAt
        +DateTime? ReleasedAt
    }

    class Order {
        <<IHasRowVersion>>
        +Guid Id
        +Guid CustomerId
        +string CustomerName
        +string? IdempotencyKey
        +Address ShippingAddress
        +decimal TotalAmount
        +OrderStatus Status
        +Guid RowVersion
        +DateTime CreatedAt
        +DateTime UpdatedAt
    }

    class OrderItem {
        +Guid Id
        +Guid OrderId
        +string ProductId
        +string ProductName
        +int Quantity
        +decimal UnitPrice
    }

    class OrderStatusHistory {
        +Guid Id
        +Guid OrderId
        +OrderStatus? FromStatus
        +OrderStatus ToStatus
        +string? Reason
        +DateTime ChangedAt
    }

    class PaymentTransaction {
        +Guid TransactionId
        +Guid OrderId
        +decimal Amount
        +PaymentMethod Method
        +string? MaskedReference
        +PaymentStatus Status
        +string? IdempotencyKey
        +DateTime? ProcessedAt
        +DateTime CreatedAt
    }

    class User {
        +Guid Id
        +string Username
        +string PasswordHash
        +UserRole Role
        +DateTime CreatedAt
    }

    class Address {
        <<owned type, no PK>>
        +string Line1
        +string? Line2
        +string City
        +string State
        +string PostalCode
        +string Country
    }

    class OrderStatus {
        <<enumeration>>
        Pending
        Confirmed
        Cancelled
        Shipped
    }
    class PaymentStatus {
        <<enumeration>>
        Pending
        Completed
        Failed
    }
    class ReservationStatus {
        <<enumeration>>
        Active
        Released
        Consumed
    }
    class PaymentMethod {
        <<enumeration>>
        CreditCard
        PayPal
        Eft
        MockGateway
    }
    class UserRole {
        <<enumeration>>
        Admin
        Customer
    }

    Customer "1" o-- "0..2" Address : Shipping/Billing (owned)
    Customer "1" --> "0..*" Order : places (CustomerId FK, no nav)
    Order "1" o-- "0..1" Address : ShippingAddress (owned, snapshot)
    Order "1" *-- "1..*" OrderItem : Items
    Order "1" *-- "0..*" OrderStatusHistory : StatusHistory
    Order "1" --> "0..*" InventoryReservation : reserved for (OrderId FK, no nav)
    Order "1" --> "0..1" PaymentTransaction : settled by (OrderId FK, no nav)
    Order --> OrderStatus
    OrderStatusHistory --> OrderStatus
    OrderItem --> Product : ProductId (no nav — snapshot only)
    ProductCategory "1" o-- "0..*" Product : Products
    ProductCategory "1" --> "0..1" ProductCategory : ParentCategory (self-ref)
    Product "1" -- "1" InventoryItem : ProductId (shared PK, no nav)
    InventoryItem "1" *-- "0..*" InventoryReservation : Reservations
    InventoryReservation --> ReservationStatus
    PaymentTransaction --> PaymentStatus
    PaymentTransaction --> PaymentMethod
    User --> UserRole
```

Two things worth calling out that aren't obvious from the diagram alone:

- **`User` has no relationship to `Customer`.** This is deliberate — see
  [auth-and-security.md](../auth-and-security.md#user-vs-customer) for the
  reasoning. They're on separate axes: `User` is *who's calling the API*,
  `Customer` is *who the order is for*.
- **Several FKs (`OrderId` on `InventoryReservation`/`PaymentTransaction`,
  `ProductId` on `OrderItem`) have no EF navigation property back to the
  parent.** That's intentional — `OrderItem`/`OrderStatusHistory` snapshot
  what they need (`ProductName`, `UnitPrice`) rather than dereferencing a
  live `Product`, so a later edit or soft-delete can't rewrite a historical
  order.

## Service / controller layer

```mermaid
classDiagram
    class IOrderService {
        <<interface>>
        +ListAsync(OrderListRequest) PagedResult~OrderResponse~
        +GetByIdAsync(Guid) OrderResponse
        +CreateAsync(CreateOrderRequest, idempotencyKey) OrderResponse
        +UpdateStatusAsync(Guid, UpdateOrderStatusRequest) OrderResponse
    }
    class OrderService {
        -AppDbContext db
        -IInventoryApiClient inventoryClient
        -IPaymentApiClient paymentClient
        -IInventoryService inventoryService
        -ILogger~OrderService~ logger
        -ReleaseAllAsync(orderId, items, token)
        -TransitionAsync(order, status, reason, token)
    }
    IOrderService <|.. OrderService

    class IInventoryService {
        <<interface>>
        +ListAsync(InventoryListRequest) PagedResult~InventoryItemResponse~
        +GetByProductIdAsync(string) InventoryItemResponse
        +ReserveAsync(string, ReserveInventoryRequest) InventoryItemResponse
        +ReleaseAsync(string, ReleaseInventoryRequest) InventoryItemResponse
        +ConsumeReservationsAsync(Guid orderId)
    }
    class InventoryService {
        -AppDbContext db
        -KeyedLockProvider lockProvider
        -ILogger~InventoryService~ logger
    }
    IInventoryService <|.. InventoryService

    class IPaymentService {
        <<interface>>
        +ListAsync(PaymentListRequest) PagedResult~PaymentTransactionResponse~
        +GetByIdAsync(Guid) PaymentTransactionResponse
        +ProcessAsync(ProcessPaymentRequest, idempotencyKey) PaymentTransactionResponse
    }
    class PaymentService {
        -AppDbContext db
        -ILogger~PaymentService~ logger
    }
    IPaymentService <|.. PaymentService

    class IProductService { <<interface>> }
    class ProductService {
        -AppDbContext db
        -IMemoryCache cache
        -ILogger~ProductService~ logger
    }
    IProductService <|.. ProductService

    class ICustomerService { <<interface>> }
    class CustomerService {
        -AppDbContext db
        -ILogger~CustomerService~ logger
    }
    ICustomerService <|.. CustomerService

    class IAuthService {
        <<interface>>
        +AuthenticateAsync(username, password) TokenResponse?
    }
    class AuthService {
        -AppDbContext db
        -IOptions~JwtOptions~ jwtOptions
        -ILogger~AuthService~ logger
    }
    IAuthService <|.. AuthService

    class IInventoryApiClient {
        <<interface>>
        +GetAsync(productId) InventoryItemResponse
        +ReserveAsync(productId, request) InventoryItemResponse
        +ReleaseAsync(productId, request) InventoryItemResponse
    }
    class InventoryApiClient {
        -HttpClient httpClient
    }
    IInventoryApiClient <|.. InventoryApiClient

    class IPaymentApiClient {
        <<interface>>
        +ProcessAsync(request, idempotencyKey) PaymentTransactionResponse
    }
    class PaymentApiClient {
        -HttpClient httpClient
    }
    IPaymentApiClient <|.. PaymentApiClient

    class OrdersController {
        +List(OrderListRequest)
        +GetById(Guid)
        +Create(CreateOrderRequest, idempotencyKey)
        +UpdateStatus(Guid, UpdateOrderStatusRequest)
    }
    class InventoryController
    class PaymentsController
    class ProductsController
    class CustomersController
    class AuthController

    OrdersController --> IOrderService
    InventoryController --> IInventoryService
    PaymentsController --> IPaymentService
    ProductsController --> IProductService
    CustomersController --> ICustomerService
    AuthController --> IAuthService

    OrderService --> IInventoryApiClient : reserve/release/check
    OrderService --> IPaymentApiClient : process
    OrderService --> IInventoryService : ConsumeReservationsAsync only
    OrderService --> AppDbContext
    InventoryService --> AppDbContext
    InventoryService --> KeyedLockProvider
    PaymentService --> AppDbContext

    class KeyedLockProvider {
        -ConcurrentDictionary~string, SemaphoreSlim~ locks
        +AcquireAsync(key) IAsyncDisposable
    }
    class AppDbContext {
        +DbSet~Order~ Orders
        +DbSet~InventoryItem~ InventoryItems
        +DbSet~PaymentTransaction~ PaymentTransactions
        +SaveChangesAsync() int
        -BumpRowVersions()
    }
    class GlobalExceptionHandler {
        <<IExceptionHandler>>
        +TryHandleAsync(HttpContext, Exception) bool
    }
    class ValidationFilter {
        <<IAsyncActionFilter>>
        +OnActionExecutionAsync(context, next)
    }
```

Note the asymmetry in how `OrderService` reaches Inventory: for the
*reserve/release/availability-check* path it goes through
`IInventoryApiClient` (a real HTTP round-trip to `InventoryController`), but
for `ConsumeReservationsAsync` — turning a payment-confirmed order's
reservations into a permanent stock decrement — it calls `IInventoryService`
directly, in-process. That's not an oversight; `ConsumeReservationsAsync`
isn't part of the spec's inventory API surface (there's no
`POST /api/inventory/{id}/consume` endpoint), it's an internal step of order
confirmation, so there's no HTTP boundary to cross for it in the first
place.

See [sequence-diagrams.md](sequence-diagrams.md) for how these classes
interact at runtime, and [flowcharts.md](flowcharts.md) for the internal
control flow of the more complex methods (`OrderService.CreateAsync`
especially).
