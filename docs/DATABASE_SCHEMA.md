# Database ownership and schema

Only ProductService, OrderService and IdentityService use SQL Server. No cross-database foreign keys or queries exist. Their `Migrations/` folders contain real EF-generated initial migrations and model snapshots. Each SQL database gets its own `__EFMigrationsHistory`.

## ShopSphereProducts (ProductService)

- **Categories**: Id int identity PK, Name nvarchar(80), unique name.
- **Products**: Id int identity PK, Name nvarchar(120), Description nvarchar(2000), Price decimal(18,2), Stock int, IsActive bit, CategoryId FK → Categories (restrict delete). Constraints require Price > 0 and Stock >= 0.
- **Reservations**: Id UUID PK (= order/request ID), Released bit.
- **ReservationItem**: Id int identity PK, ReservationId FK → Reservations (cascade), ProductId int, ProductName nvarchar(120), UnitPrice decimal(18,2), Quantity int. Product identity is retained even after deactivation; snapshots survive later edits.

Admin product edits preserve current stock; inventory changes use a separate atomic delta endpoint with limits. Public queries exclude inactive products. Reservation/release transactions are serializable and idempotent by order ID.

## ShopSphereOrders (OrderService)

- **Orders**: Id UUID PK, UserId nvarchar(36), Email nvarchar(254), ShippingAddress nvarchar(500), CreatedUtc datetime2, Status nvarchar(30), NotificationSent bit, LastAttemptUtc nullable datetime2, LeaseUntilUtc nullable datetime2. Index on (UserId, CreatedUtc).
- **OrderItem**: Id int identity PK, OrderId FK → Orders (cascade), ProductId int, ProductName nvarchar(120), UnitPrice decimal(18,2), Quantity int > 0.

UserId/ProductId are remote identifiers, not foreign keys to other databases. Total is calculated from immutable item snapshots. Pending intents initially have empty item names and zero prices until inventory is confirmed. Orders retain those placeholders if rejected. UTC timestamps are used consistently.

## ShopSphereIdentity (IdentityService)

- **Users**: Id UUID PK, Email nvarchar(254) unique and normalized uppercase, Name nvarchar(100), PasswordHash nvarchar(max), Role nvarchar(30).

No plaintext passwords are stored. New registrations always receive Customer. Generated development credentials are supplied outside source control; seeding is explicit and development-only.

## notifications.db (NotificationService)

- **Notifications**: Id UUID PK, OrderId UUID unique, UserId text, Email text (configured maximum 254), Message text, CreatedUtc datetime.

This is a separate local SQLite database used as a development delivery sink, not one of the three SQL Server business databases. It records confirmations and provides user-scoped history; it never reads the order database. `EnsureCreatedAsync` creates missing schema without deleting data; schema evolution for this optional local sink requires a future migration if the model changes.

## Initialization

Apply each service's EF migration explicitly, then opt into development seed flags. No code calls `EnsureDeleted`, drops databases or truncates tables. Seed products only populate an empty catalog; seed users are inserted only if their normalized email is missing. Migrations may be applied automatically only when `Database__MigrateOnStartup=true` is explicitly set. Do not use simultaneous seed processes.
