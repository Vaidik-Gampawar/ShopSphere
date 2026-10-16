# Verification performed

Verified on 16 October 2026 using the locally installed .NET SDK 10.0.402 on macOS ARM64. Application packages target .NET 10 and use Microsoft ASP.NET Core/EF Core 10.0.13.

| Check | Actual result |
| --- | --- |
| `dotnet build ShopSphere.sln` | Succeeded, 0 warnings, 0 errors |
| EF `migrations add InitialCreate` for Product, Identity, Order | Generated successfully for each service |
| SQL Server migration/model consistency | All three have migrations, no pending model changes, SQL table scripts generate offline |
| `dotnet test ShopSphere.sln --logger 'trx;LogFileName=shopsphere.trx'` | 33 passed, 0 failed, 0 skipped |
| `dotnet list ShopSphere.sln package --vulnerable --include-transitive` | No vulnerable packages reported for all seven projects by the configured NuGet source |
| Local settings helper in a temporary directory | Three connections generated; permissions 0600; existing settings preserved |
| `bash -n scripts/migrate.sh` | Passed |

Tests include API requests against real ASP.NET TestServer applications with isolated SQLite relational databases, fake HTTP dependency failures for recovery checks, and two MVC journeys. The customer journey uses actual ProductGateway/NotificationGateway HTTP requests to the service TestServers, not mocked purchase results. It registers a customer, signs into MVC, adds a real catalog item, checks out, verifies stock, order history and notification history, and confirms that customer access to admin is denied. The admin journey signs in using a hashed test account, renders management pages, creates a product, adjusts inventory, creates a category, and verifies the changes through ProductService.

The first restore caught vulnerable transitive System.Security.Cryptography.Pkcs 9.0.11. The application now pins patched 10.0.13 explicitly; the subsequent build/audit passed. An initial test run before migrations existed failed migration discovery; that check passed after real migrations were added.

## Not verified here

No SQL Server instance or database credentials were provided. SQL Server migrations were **generated and checked offline, not applied to a live SQL Server**. Development sample seeding and live SQL Server connectivity, locking/deadlock behavior, collation, and permissions therefore require the local smoke test in LOCAL_SETUP.md. The automated tests use SQLite to verify relational behavior but do not prove provider-specific SQL Server concurrency semantics. Browser layouts were not visually inspected; Razor rendering, forms, cookie authentication, antiforgery and HTTP workflows were exercised by the MVC integration tests.

No CI/CD, Dockerfiles, container orchestration or deployment configuration was created.
