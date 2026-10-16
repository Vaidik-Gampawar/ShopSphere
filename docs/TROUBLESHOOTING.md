# Troubleshooting and verification

- **SDK NETSDK1045**: `dotnet --info` must show .NET 10. Use the SDK installed at `$HOME/.dotnet` by putting it first on PATH, or install .NET 10 on your machine. The repository targets net10.0 and does not fall back to .NET 8.
- **Missing Jwt:Key / connection**: source `.local/dev.env` in each terminal and supply the correct service's `ConnectionStrings__Database`. All APIs validate the same signing key/issuer/audience. Do not log secrets.
- **SQL connection/login/certificate errors**: verify server host, TCP port, network access, login permissions and local development certificate settings. This repo does not start a database server. Migration credentials must have schema privileges. Each database name differs.
- **Invalid object / no tables**: apply the owning service's migration. Enabling sample seeding does not implicitly migrate. The database settings must point to the same database for both migration and runtime.
- **Empty catalog/no demo login**: run Product and Identity once with `Database__Seed=true` in Development, after migration. Identity also needs both sample credentials. Existing users keep their original password hashes even if environment passwords change.
- **403 from internal calls**: check the same `Services__Key` value in Product, Order and Notification. End users must not call internal endpoints.
- **401 after an hour**: sign in again. MVC cookie and JWT expire together. Check clocks/issuer/audience/signature if login fails immediately.
- **Order pending during outage**: start ProductService and NotificationService, then leave OrderService running. It retries every 15 seconds. After a crash, a processing lease may need up to two minutes to expire. Refresh order details. Do not resubmit with a new request ID while the result is uncertain.
- **Confirmation pending**: an order can be confirmed while NotificationService is down. The persisted pending flag is retried. The local API records one confirmation per order.
- **Rejected order**: inventory/product validation failed; no payment was collected. Adjust the cart and start a fresh purchase. Details remain in history.
- **Cancellation pending**: release awaits ProductService. The recovery worker completes cancellation when release succeeds. Shipped/processing orders cannot be cancelled in this practice workflow.
- **Cart disappeared**: session state is process-local and is cleared on sign-in/sign-out. Restarting MVC loses carts, not persisted orders. Keep browser cookies enabled.
- **Antiforgery 400**: reload the form after signing in/out or changing cookies. Always submit the generated token with its matching session cookie.
- **Port already used**: stop the other process or change launch settings and `Services__*` URLs together.
- **Local notification file**: use an absolute `ConnectionStrings__Notifications` path for predictable persistence. The file must be writable by the service process.

## Test coverage

`dotnet test ShopSphere.sln` runs xUnit tests with separate SQLite in-memory relational databases and ASP.NET TestServer hosts. Coverage includes product create/update/deactivation, paging/search, validation/problem responses, inventory reservation rollback/idempotency/release, password hashing and login, token signature/expiry/audience and roles, order snapshot creation/replays/ownership/status transitions, service outage recovery, notification retry, MVC antiforgery, administrator product/category/inventory forms, and a complete MVC registration → cart → checkout → history/notifications journey through actual HTTP handlers for all four services.

Tests replace SQL Server contexts explicitly; no development database is accessed. Unit-level validation/password logic is exercised along with API and relational persistence tests. SQL Server provider models/migration discovery/scripts are verified offline. To verify provider-specific locking, SQL precision, permissions and live SQL Server connectivity, follow the local setup against a separate SQL Server and execute the catalog/login/checkout/admin smoke flow. SQLite tests cannot prove SQL Server locking behavior.

See `docs/VERIFICATION.md` for the actual commands and results from this implementation session.
