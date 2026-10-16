# Dockerfiles

There is one independent multi-stage Dockerfile for each runnable application. Build with the **repository root as context**, because every application references `src/Shared` and `Directory.Build.props`.

```sh
docker build -f src/ShopSphere.Web/Dockerfile -t shopsphere-web:local .
docker build -f src/ProductService/Dockerfile -t shopsphere-product:local .
docker build -f src/OrderService/Dockerfile -t shopsphere-order:local .
docker build -f src/IdentityService/Dockerfile -t shopsphere-identity:local .
docker build -f src/NotificationService/Dockerfile -t shopsphere-notification:local .
```

Images use Microsoft's .NET 10 SDK for compilation and ASP.NET Core runtime for execution. Runtime containers use the provided non-root `app` user and listen on port **8080**. `launchSettings.json` is not used when executing a published DLL. `.dockerignore` excludes local secrets, databases, Git metadata, tests and build output. Secrets must be supplied at runtime, never as image build arguments.

## Runtime settings

Use the settings in [LOCAL_SETUP.md](LOCAL_SETUP.md), with addresses reachable **from inside each container**. SQL Server remains separate and must already have the migrations applied. The standard runtime image has no SDK or EF command-line tool. Existing local migration commands can be run from the host; alternatively explicitly opt into the application's `Database__MigrateOnStartup=true` setting.

- All four APIs need the same `Jwt__Key`; issuer/audience defaults match the application.
- Product, Order and Notification need the same `Services__Key` for internal calls.
- Each SQL-backed API needs its own `ConnectionStrings__Database`.
- MVC needs `Services__Product`, `Services__Order`, `Services__Identity`, `Services__Notification`.
- Order needs `Services__Product` and `Services__Notification`.
- Override `AllowedHosts` with semicolon-separated expected host names, including any container DNS names used by callers. Current JSON defaults allow only localhost and 127.0.0.1. For unrestricted local experimentation, `AllowedHosts=*` is also supported.
- `ASPNETCORE_ENVIRONMENT` defaults to Production; development seeding only runs when explicitly changed to Development and `Database__Seed=true`. Identity seeding also requires both configured sample accounts. No passwords are included in images.

Example ProductService command, assuming you have exported the secrets and a container-reachable Product database connection locally:

```sh
docker run --rm --name shopsphere-product \
  -p 5101:8080 \
  -e Jwt__Key \
  -e Services__Key \
  -e ConnectionStrings__Database \
  -e 'AllowedHosts=localhost;127.0.0.1;shopsphere-product' \
  shopsphere-product:local
```

`-e NAME` forwards that exported shell variable. The `ProductConnection` helper from local setup must first be assigned to `ConnectionStrings__Database` using an address reachable from the container. On Docker Desktop, a host service is commonly reachable at `host.docker.internal`; other containers on a user-created Docker network are addressed by their container names and **8080**, e.g. `http://shopsphere-product:8080`. A container's `localhost` points to that container itself, so the application's default localhost backend URLs must be overridden.

Map the other services similarly: Web `5100:8080`, Order `5102:8080`, Identity `5103:8080`, Notification `5104:8080`. All expose `/health` for liveness.

NotificationService defaults its SQLite file to `/app/data/notifications.db`. Persist that path with `--mount type=volume,source=shopsphere-notifications,target=/app/data` so confirmations survive container removal. The image creates a writable data directory for the non-root user. For MVC, preserve authentication encryption keys across container replacement with a writable persistent mount at `/home/app/.aspnet/DataProtection-Keys`. Frontend carts remain in process memory.

These files provide image builds only. No Compose, orchestration, pipelines, or database containers are configured.

## Verification limit

The Docker CLI is installed on the development machine, but its engine was not running when these files were added. Dockerfile source paths, stage references, project names and entry points were checked locally; **image builds and container execution have not been verified**. Build with the commands above once Docker Engine is running.

Reference: [Microsoft's ASP.NET Core Docker guide](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0).
