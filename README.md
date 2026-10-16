# ShopSphere

A functional ASP.NET Core MVC shop with four independent HTTP API services, built for practicing DevOps around an existing application. Targets .NET 10; uses SQL Server, EF Core, Razor, vendored Bootstrap 5.3.8, JWT bearer authentication, secure MVC cookies, and xUnit.

## Repository

```text
ShopSphere.sln
src/
  ShopSphere.Web/       MVC frontend :5100 (HTTP APIs only)
  ProductService/       Catalog, categories, inventory :5101
  OrderService/         Orders, snapshots, recovery worker :5102
  IdentityService/      Registration, password hashing, JWT :5103
  NotificationService/ Local confirmation history :5104
  Shared/               DTO contracts and common API infrastructure
tests/ShopSphere.Tests/ Unit, API/database integration, complete MVC checkout
docs/                  Architecture, schemas, setup, API reference, troubleshooting
scripts/               Local secret generation and database migration commands
```

## Start here

Follow [local setup](docs/LOCAL_SETUP.md) for exact prerequisites, secret generation, migrations, sample accounts, and five separate run commands. SQL Server must be reachable separately; no container runtime is required by the application.

```sh
dotnet restore ShopSphere.sln
dotnet build ShopSphere.sln
dotnet test ShopSphere.sln
```

The test suite uses separate in-memory SQLite databases. It never connects to your development SQL Server. SQL Server migrations and models are also checked without connecting to a server.

## Implemented

- Responsive home, catalog search/category filtering/pagination, details, account dashboard.
- Session shopping cart, quantity changes, checkout, purchase price/name snapshots, per-user history and confirmation history.
- Registration/login, ASP.NET password hashing, signed and validated JWTs, administrator/customer roles, protected APIs, antiforgery-protected MVC forms and HTTP-only authentication cookies.
- Administrator product creation/editing/deactivation, category creation/renaming, stock adjustments, order status transitions and cancellation with stock restoration.
- Transactional inventory reservations, request idempotency, persisted order intents, retry recovery after service interruptions, idempotent local notifications.
- DTOs, validation, problem responses, structured logging, health endpoints, independent configurations and migrations, opt-in development seeding.

## Documentation

- [Architecture and consistency](docs/ARCHITECTURE.md)
- [Database schema](docs/DATABASE_SCHEMA.md)
- [Local setup](docs/LOCAL_SETUP.md)
- [API endpoints](docs/API_ENDPOINTS.md)
- [Troubleshooting and verification](docs/TROUBLESHOOTING.md)

This is a practice storefront: it does not collect payments, calculate taxes or shipping, or send real email. Optional per-application Dockerfiles are provided; see [Docker build and runtime settings](docs/DOCKER.md). Orchestration and CI/CD remain outside scope. See the architecture document for further limits.

## Verified result

The solution builds with zero warnings/errors and all **33 tests pass**. NuGet reports no known vulnerable dependencies. SQL Server migrations were generated and checked offline; no live SQL Server was available to apply them. See [verification details](docs/VERIFICATION.md).
