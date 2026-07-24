# Documentation Index

Documentation for the Genasys Order Processing Service, split into
focused, single-purpose docs rather than one long file.

| Doc | Covers |
|---|---|
| [plan/order-processing-service-plan.md](plan/order-processing-service-plan.md) | The original design plan — data model/ERD, API surface, order-creation flow, error scenarios, key decisions and completion status. Written before/during the build; kept as the historical record of *what was decided and why*. |
| [architecture.md](architecture.md) | Project structure, composition root, layered request flow, and a decisions table (plain services vs. CQRS, no generic repository, domain exceptions vs. `Result<T>`, etc.) |
| [diagrams/class-diagram.md](diagrams/class-diagram.md) | UML class diagrams — the persisted domain model, and the service/controller/client layer |
| [diagrams/sequence-diagrams.md](diagrams/sequence-diagrams.md) | Runtime sequence diagrams — auth, order creation (happy path, declined, service unavailable), idempotent replay |
| [diagrams/flowcharts.md](diagrams/flowcharts.md) | Branch-by-branch control flow for the non-trivial service methods, especially `OrderService.CreateAsync` |
| [auth-and-security.md](auth-and-security.md) | JWT issuance/validation, role model, password hashing, `User` vs `Customer`, how inter-service calls stay authenticated |
| [resilience-and-consistency.md](resilience-and-consistency.md) | Idempotency, retries and the non-idempotent-reservation risk, cancellation-safe compensation, concurrency — each as a concrete use case |
| [testing.md](testing.md) | What's tested and how (unit vs. integration), and an honest list of what's deliberately not yet covered |

## Where to start

- **Want the "why" behind the data model and API design?** →
  [plan/order-processing-service-plan.md](plan/order-processing-service-plan.md)
- **Want to understand the codebase's shape before reading source?** →
  [architecture.md](architecture.md), then
  [diagrams/class-diagram.md](diagrams/class-diagram.md)
- **Want to trace what happens on `POST /api/orders`?** →
  [diagrams/sequence-diagrams.md](diagrams/sequence-diagrams.md) for the
  cross-service view, [diagrams/flowcharts.md](diagrams/flowcharts.md) for
  the in-method branching
- **Evaluating the distributed-systems handling specifically (retries,
  idempotency, cancellation)?** → [resilience-and-consistency.md](resilience-and-consistency.md)
- **Checking test coverage?** → [testing.md](testing.md)

Root [`README.md`](../README.md) has the practical run/try-it-out
instructions and isn't duplicated here.
