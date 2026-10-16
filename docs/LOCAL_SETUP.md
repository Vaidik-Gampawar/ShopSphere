# Local setup

## Prerequisites

- .NET 10 SDK (`dotnet --version` must start with `10.`), Python 3 for the optional local secret helper, and Bash for the optional migration helper.
- A reachable Microsoft SQL Server instance (SQL Server 2022 or newer recommended). Use a local Windows/Linux instance or an independently provisioned server. SQL Server is not installed by this repository; on macOS connect to a separate server. Docker is optional and managed independently by you.
- A SQL login allowed to create the three development databases, or an administrator must pre-create them and grant your login schema migration permissions. Do not reuse production databases.

On the machine where this project was created, a .NET 10 SDK was installed at `$HOME/.dotnet`; use `export PATH="$HOME/.dotnet:$PATH"` if your normal `dotnet` still resolves to .NET 8. On a new machine install the .NET 10 SDK using Microsoft's installer.

All commands below run from the repository root.

## 1. Restore and build

```sh
export PATH="$HOME/.dotnet:$PATH"  # only if your SDK is installed here
dotnet --version
dotnet restore ShopSphere.sln
dotnet tool restore
dotnet build ShopSphere.sln
```

Package versions are pinned in project files. The local EF tool version matches EF Core. `global.json` allows installed .NET 10 feature bands.

## 2. Generate local settings

Set your own SQL connection string without `Database` / `Initial Catalog`. For SQL authentication, enter the password locally; do not commit it. To avoid putting it in shell history:

```sh
read -r -s -p 'SQL password: ' SQL_PASSWORD
export SHOPSPHERE_SQL="Server=localhost,1433;User Id=shopsphere_dev;Password=$SQL_PASSWORD;Encrypt=True;TrustServerCertificate=True"
python3 scripts/create-local-env.py
unset SQL_PASSWORD SHOPSPHERE_SQL
source .local/dev.env
```

The `read -p` example uses Bash. In zsh use `read -s 'SQL_PASSWORD?SQL password: '` instead. If your password contains a semicolon or quotes, use a correctly quoted SQL Server connection-string value (or edit `.local/dev.env` with proper shell quoting). Windows users can use Git Bash or manually set the same variables in PowerShell with `$env:Name='value'`.

For Windows integrated authentication, a suitable base string is `Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True`. `TrustServerCertificate=True` is appropriate only for a trusted local development server.

The helper generates `.local/dev.env`, which Git ignores and which is created with mode 0600. Existing settings are preserved. It contains:

| Variable | Purpose |
| --- | --- |
| `Jwt__Key` | Shared random signing/validation key, at least 32 UTF-8 bytes; all four APIs need the same value |
| `Services__Key` | Separate random internal HTTP API key, at least 32 bytes; Product, Order, Notification share it |
| `ProductConnection` | SQL Server connection to `ShopSphereProducts` |
| `OrderConnection` | SQL Server connection to `ShopSphereOrders` |
| `IdentityConnection` | SQL Server connection to `ShopSphereIdentity` |
| `Seed__Admin__Email`, `Seed__Admin__Password` | Generated local administrator credentials |
| `Seed__Customer__Email`, `Seed__Customer__Password` | Generated local customer credentials |
| `ASPNETCORE_ENVIRONMENT` | Development (required for sample seeding) |

These connection helper variables are passed to the standard `ConnectionStrings__Database` setting for each process. API `appsettings.json` files contain no database passwords or signing secrets. You can alternatively use ASP.NET environment variables or user secrets. Environment variables override JSON using double underscores for nested keys.

## 3. Apply SQL Server migrations

```sh
bash scripts/migrate.sh
```

Equivalent explicit commands after sourcing `.local/dev.env`:

```sh
ConnectionStrings__Database="$ProductConnection" dotnet ef database update --project src/ProductService
ConnectionStrings__Database="$IdentityConnection" dotnet ef database update --project src/IdentityService
ConnectionStrings__Database="$OrderConnection" dotnet ef database update --project src/OrderService
```

Each service owns its migration and model snapshot. These commands create missing databases and apply pending migrations; they do not drop existing databases. Startup migration is off by default. `Database__MigrateOnStartup=true` explicitly opts into applying migrations on service startup. Never use `database drop` to update data.

NotificationService owns a local SQLite file `notifications.db` in its content/working directory. It creates missing tables non-destructively at startup. Override `ConnectionStrings__Notifications` with an absolute SQLite path to keep it in a specific location. No paid email account is needed.

To add a future migration after changing a service model:

```sh
ConnectionStrings__Database="$ProductConnection" dotnet ef migrations add DescribeChange --project src/ProductService
```

## 4. Run five terminals

In **each** terminal, change to the repository root and `source .local/dev.env` first. Each service can be built and run independently. Service-to-service operations need their dependencies running, but Product catalog/Identity login are independently usable.

ProductService (terminal 1):

```sh
ConnectionStrings__Database="$ProductConnection" Database__Seed=true dotnet run --project src/ProductService
```

IdentityService (terminal 2):

```sh
ConnectionStrings__Database="$IdentityConnection" Database__Seed=true dotnet run --project src/IdentityService
```

OrderService (terminal 3):

```sh
ConnectionStrings__Database="$OrderConnection" dotnet run --project src/OrderService
```

NotificationService (terminal 4):

```sh
dotnet run --project src/NotificationService
```

Frontend (terminal 5):

```sh
dotnet run --project src/ShopSphere.Web
```

Open [ShopSphere](http://localhost:5100). Open `.local/dev.env` privately to read your generated demo passwords. Sign in with `customer@shopsphere.test` to shop, and `admin@shopsphere.test` for administration. You can also register a new customer in the UI. Existing seeded users are never overwritten or assigned new passwords on restart. Existing product catalogs are not overwritten. Run seeding with a single process at a time.

Defaults are local HTTP development ports 5100–5104 in `Properties/launchSettings.json`. With `--no-launch-profile`, provide `--urls http://localhost:5101` (and corresponding service port) yourself. Cookie security uses the request scheme; protect any nonlocal environment with HTTPS.

Backend URLs in each `appsettings.json` and the frontend use these settings:

```sh
export Services__Product=http://localhost:5101
export Services__Order=http://localhost:5102
export Services__Identity=http://localhost:5103
export Services__Notification=http://localhost:5104
```

The browser calls MVC only; MVC calls HTTP APIs server-side. JWT tokens remain in an encrypted HTTP-only authentication cookie. Browser storage is not used for tokens. Frontend session state is in memory and resets on restart.

## 5. Verify

```sh
curl -f http://localhost:5101/health
curl -f http://localhost:5102/health
curl -f http://localhost:5103/health
curl -f http://localhost:5104/health
curl -f http://localhost:5100/health
curl -f 'http://localhost:5101/api/products?page=1&pageSize=3'
dotnet test ShopSphere.sln
```

Health endpoints check process liveness, not database readiness. For a database smoke test, retrieve the catalog, register/login, place an order, check history/notifications, and adjust inventory as an administrator. Use `dotnet test ShopSphere.sln --logger 'trx;LogFileName=shopsphere.trx'` for a test report.
