# Auth & Security

The spec lists JWT auth as a **bonus**, not mandatory. This covers it
without an external identity provider in the run path — `dotnet run` is
still the entire setup story, no container, no realm import.

## Token issuance

`POST /api/auth/token` (`AuthController` → `AuthService.AuthenticateAsync`,
[AuthService.cs](../src/Genasys.Api/Services/AuthService.cs)) is the only
`[AllowAnonymous]` endpoint in the app. It:

1. Looks up the `User` by username (`Data/Seed/DataSeeder.cs` seeds two on
   startup — see [testing.md](testing.md) for the credentials).
2. Verifies the password with `BCrypt.Net.BCrypt.Verify` against
   `User.PasswordHash` — **passwords are never stored or compared in
   plaintext**.
3. On success, signs a `JwtSecurityToken` (HS256) with claims `sub` (user
   id), `name`, `role`, and a `jti` (unique token id), using a symmetric key
   from `JwtOptions.Key` (`appsettings.json`, section `Jwt`).
4. Returns `{ accessToken, tokenType: "Bearer", expiresIn }`.

A failed lookup or a failed `BCrypt.Verify` both return the same generic
`null` → `401` — the endpoint doesn't distinguish "unknown username" from
"wrong password" in its response, so it can't be used to enumerate valid
usernames.

## Token verification

Every other endpoint is protected by the standard
`Microsoft.AspNetCore.Authentication.JwtBearer` middleware
(`WebApplicationBuilderExtensions.ConfigureAuthentication`), which validates
issuer, audience, signature, and lifetime (30s clock skew). There's no
`[Authorize]` attribute needed on most controllers/actions — authorization
is **default-deny**:

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

(`WebApplicationBuilderExtensions.ConfigureAuthorization`). Any endpoint
without an explicit policy still requires *some* authenticated caller; only
`[AllowAnonymous]` on the token endpoint opts out.

## Role model

Two roles (`UserRole.Admin`, `UserRole.Customer`), checked via
`[Authorize(Roles = nameof(UserRole.Admin))]` on mutating endpoints for
`Product`/`Customer`, and on the manual `PUT /api/orders/{id}/status`
override:

| Endpoint category | Who can call it |
|---|---|
| All `GET` endpoints | Any authenticated user |
| `POST /api/orders` | Any authenticated user — this *is* the transactional flow the assessment is built around, not an admin action |
| `POST /api/inventory/{id}/reserve`, `/release` | Any authenticated user — operational, called by `OrderService` on behalf of whoever is placing the order |
| `POST /api/payments/process` | Any authenticated user — same reasoning |
| `POST`/`PUT`/`DELETE` on `Product`, `Customer` | `Admin` only |
| `PUT /api/orders/{id}/status` | `Admin` only — a manual override, distinct from the automatic transitions the order flow drives itself |

The middle three rows are the ones worth defending explicitly, because
they look under-protected at first glance: any authenticated caller — not
just `Admin` — can call `Reserve`/`Release`/`Process` directly, not only
through `OrderService`. That's intentional (see comments at
[InventoryController.cs:22](../src/Genasys.Api/Controllers/InventoryController.cs:22)
and [OrdersController.cs:23](../src/Genasys.Api/Controllers/OrdersController.cs:23)) —
gating them to `Admin` would break the legitimate internal
`OrderService → InventoryController`/`PaymentsController` call, since that
call carries the *placing customer's* token, not a separate admin identity
(see below). The honest tradeoff: a non-admin authenticated user can also
call these endpoints standalone, creating a reservation or a payment record
without going through `OrderService`'s validation. That's flagged as a known
gap, not an oversight — closing it properly needs a distinct
service-to-service credential separate from end-user tokens, which is more
naturally a round-2 change than a patch to this round's auth model.

## User vs Customer

`User` (the JWT login principal) and `Customer` (who an order is placed
for) are deliberately **separate entities with no relationship between
them**:

| Term | Maps to | Answers |
|---|---|---|
| **User** | `Entities/User.cs` | *Who's authenticating* — the credential holder calling the API |
| **Customer** | `Entities/Customer.cs` | *Who the order is for* — a commerce entity with addresses and order history |

A lot of consumer SaaS collapses these into one row (`account == customer
profile`), which is where the instinct to merge them comes from. Here
they're kept apart because the `User`s in this system act on behalf of many
`Customer`s (an admin/support agent manages orders for hundreds of
customers), so folding `User` into `Customer` would force every customer to
carry login credentials they don't need. `User` is deliberately minimal —
no self-registration endpoint, no `UserController`, seeded at startup only —
because it exists to make `[Authorize(Roles = "Admin")]` mean something
against a real seeded record, not to be a full account system. Customer
self-service login is a clean future extension (a nullable `CustomerId` on
`User`), not needed for this round.

## Inter-service call identity

`OrderService` calls `InventoryController`/`PaymentsController` over real
HTTP (loopback), which lands back in this same app's `[Authorize]`
pipeline — those requests need a valid bearer token too, or they'd get
`401`ed by their own middleware. `Common/AuthHeaderPropagationHandler.cs` (a
`DelegatingHandler` attached to both typed `HttpClient`s) solves this by
**forwarding the original caller's `Authorization` header** onto the
outbound request, rather than inventing a separate service-account
identity:

```csharp
var authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
if (!string.IsNullOrEmpty(authHeader))
{
    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
}
```

This is *why* the role model above works the way it does — Inventory and
Payment see the same principal that called `OrderService`, not a
privileged internal identity, so gating those endpoints to `Admin` would
directly break customer-placed orders (most customers aren't Admins).

## Security practices covered

| Practice | Where |
|---|---|
| Password hashing | BCrypt (`AuthService`, `DataSeeder`) — never plaintext, never reversible |
| No raw payment credentials | `PaymentTransaction.MaskedReference` only (`**** 4242` shape) — see `PaymentService.MaskReference` |
| Signed, expiring tokens | HS256, configurable expiry (`JwtOptions.ExpiryMinutes`, default 60) |
| Default-deny authorization | `FallbackPolicy.RequireAuthenticatedUser()` — opt-out (`[AllowAnonymous]`), not opt-in |
| Role-gated mutations | `Admin`-only on Product/Customer writes and manual order-status override |
| No internal error leakage | `GlobalExceptionHandler` maps unhandled exceptions to a generic `500` `ProblemDetails` — stack traces never reach the client |

## Not covered (known gaps)

- **No refresh tokens** — a token simply expires after `ExpiryMinutes`; the
  client re-authenticates. Fine for an assessment, not production-shaped.
- **No service-to-service credential** — covered above; Inventory/Payment
  mutation endpoints trust any authenticated caller, not just requests that
  actually came from `OrderService`.
- **No token revocation** — a compromised token is valid until it expires;
  there's no blocklist/allowlist.
- **No rate limiting on `/api/auth/token`** — nothing currently throttles
  repeated login attempts.
