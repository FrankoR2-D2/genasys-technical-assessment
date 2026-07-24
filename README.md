# Genasys Order Processing Service

Order, Inventory, and Payment API built for the Genasys C# Developer technical
assessment. ASP.NET Core Web API (.NET 8) with EF Core InMemory, JWT auth,
FluentValidation, and typed `HttpClient` + Polly for inter-service calls.

Full design plan — data model, order-creation flow, sequence diagrams, error
handling, architecture decisions, completion status — lives at
[`docs/plan/order-processing-service-plan.md`](docs/plan/order-processing-service-plan.md).
Deeper
documentation — architecture & patterns, UML/sequence/flowchart diagrams,
auth implementation, and a use-case writeup of idempotency/retries/
cancellation-safe compensation — is indexed at
[`docs/README.md`](docs/README.md).

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
No database, no Docker — the InMemory provider means `dotnet run` is the
entire setup story.

```bash
dotnet run --project src/Genasys.Api
```

The API listens on `http://localhost:5148` (see
`src/Genasys.Api/Properties/launchSettings.json`). Swagger UI is at
`http://localhost:5148/swagger` and opens automatically in Development.

## Authenticate

Every endpoint requires a bearer token except `POST /api/auth/token`. Two
users are seeded on startup:

| Username | Password    | Role     |
|----------|-------------|----------|
| `admin`  | `Admin123!` | Admin    |
| `viewer` | `Viewer123!`| Customer |

`Admin` is required for Product/Customer create/update/delete and for the
manual order-status override; every other endpoint accepts any authenticated
caller. In Swagger, click **Authorize** and paste the `accessToken` from the
token response (no `Bearer ` prefix needed — Swagger adds it).

```bash
curl -X POST http://localhost:5148/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin123!"}'
```

## Try the order flow

Dummy data (5 products, 3 customers, categories) is seeded on every startup.
List products/customers to get real IDs, then place an order:

```bash
TOKEN=$(curl -s -X POST http://localhost:5148/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin123!"}' | jq -r .accessToken)

CUSTOMER_ID=$(curl -s http://localhost:5148/api/customers \
  -H "Authorization: Bearer $TOKEN" | jq -r '.items[0].id')

curl -s -X POST http://localhost:5148/api/orders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"customerId\":\"$CUSTOMER_ID\",\"items\":[{\"productId\":\"SKU-001\",\"quantity\":2}]}"
```

To exercise the failure path deterministically, add
`"paymentInstrumentReference":"DECLINE"` to the request body — the simulated
gateway declines, and the order is created and cancelled with inventory
released, rather than silently disappearing.

## Test

```bash
dotnet test
```

31 tests: `OrderServiceTests` and `InventoryServiceTests` exercise the order
flow's business logic directly (happy path, insufficient inventory, payment
decline, upstream service unavailable, cancellation-safe compensation,
idempotent retry, invalid status transitions, and a concurrency test proving
two simultaneous reservations for the same product can't oversell).
`ConcurrencyTests` proves the `RowVersion` optimistic-concurrency mechanism
itself. `Integration/*` spins up the real HTTP pipeline via
`WebApplicationFactory` to check auth, role-based authorization, validation,
CRUD lifecycle, a genuine concurrent idempotency-key race, soft-delete
interaction with historical orders, and pagination edge cases. See
[`docs/testing.md`](docs/testing.md) for the full breakdown, including two
real concurrency bugs the test suite itself found and fixed.

## What's here vs. what's deferred

Beyond the assessment's three required controllers (Order, Inventory,
Payment), this also builds out Product and Customer as full CRUD resources,
an `AuthController` for the JWT bonus, list/search/pagination on every
collection endpoint, and the production-shaped touches documented in the
plan (audit trail, reservation ledger, soft delete, idempotency, optimistic
concurrency). An earlier Postgres/Keycloak/multi-tenant identity layer was
explored for this repo and removed in favor of the spec-first design — see
§10 of the plan doc.
