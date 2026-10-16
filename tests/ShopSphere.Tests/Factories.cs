extern alias product;
extern alias identity;
extern alias order;
extern alias notification;
extern alias web;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NotificationService;
using OrderService;
using ProductService;

namespace ShopSphere.Tests;

public static class TestConfig
{
    public const string Key = "Tests-only-signing-key-with-more-than-32-characters!";
    public const string ServiceKey = "Tests-only-service-key-with-more-than-32-characters!";

    public static void Configure(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        foreach (
            var pair in new Dictionary<string, string>
            {
                { "Jwt:Key", Key },
                { "Jwt:Issuer", "ShopSphere.Identity" },
                { "Jwt:Audience", "ShopSphere" },
                { "Services:Key", ServiceKey },
                { "Services:Product", "http://localhost" },
                { "Services:Order", "http://localhost" },
                { "Services:Identity", "http://localhost" },
                { "Services:Notification", "http://localhost" },
                {
                    "ConnectionStrings:Database",
                    "Server=unused;Database=tests;Integrated Security=True;TrustServerCertificate=True"
                },
            }
        )
            b.UseSetting(pair.Key, pair.Value);
    }

    public static void Database<T>(IServiceCollection services, SqliteConnection connection)
        where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.RemoveAll<T>();
        services.AddDbContext<T>(o => o.UseSqlite(connection));
    }

    public static void Init<T>(IServiceProvider provider)
        where T : DbContext
    {
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<T>().Database.EnsureCreated();
    }

    public static string Token(
        string role = "Customer",
        string? userId = null,
        string key = Key,
        string audience = "ShopSphere",
        bool expired = false
    )
    {
        var t = new JwtSecurityToken(
            "ShopSphere.Identity",
            audience,
            [
                new Claim("sub", userId ?? Guid.NewGuid().ToString()),
                new Claim("name", "Test User"),
                new Claim("email", "test@example.test"),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddMinutes(expired ? -5 : 30),
            signingCredentials: new(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(t);
    }

    public static HttpClient As(this HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }
}

public class ProductFactory : WebApplicationFactory<product::Program>
{
    readonly SqliteConnection connection = new("Data Source=:memory:");

    public ProductFactory()
    {
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        TestConfig.Configure(b);
        b.ConfigureServices(s => TestConfig.Database<ProductDb>(s, connection));
    }

    public HttpClient Start()
    {
        var c = CreateClient();
        TestConfig.Init<ProductDb>(Services);
        return c;
    }

    public async Task<int> Seed(int stock = 10)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDb>();
        var p = new Product
        {
            Name = "Test headphones",
            Description = "Test product",
            Price = 29.95m,
            Stock = stock,
            Category = new() { Name = "Technology" },
        };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            connection.Dispose();
    }
}

public class IdentityFactory : WebApplicationFactory<identity::Program>
{
    readonly SqliteConnection connection = new("Data Source=:memory:");

    public IdentityFactory()
    {
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        TestConfig.Configure(b);
        b.ConfigureServices(s => TestConfig.Database<IdentityDb>(s, connection));
    }

    public HttpClient Start()
    {
        var c = CreateClient();
        TestConfig.Init<IdentityDb>(Services);
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            connection.Dispose();
    }
}

public class NotificationFactory : WebApplicationFactory<notification::Program>
{
    readonly SqliteConnection connection = new("Data Source=:memory:");

    public NotificationFactory()
    {
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        TestConfig.Configure(b);
        b.ConfigureServices(s => TestConfig.Database<NotificationDb>(s, connection));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            connection.Dispose();
    }
}

public class FakeProducts : IProductGateway
{
    public bool Unavailable { get; set; }
    public bool OutOfStock { get; set; }
    public int Reservations { get; set; }
    public int Releases { get; set; }

    public Task<ShopSphere.Shared.ReservationDto> Reserve(
        ShopSphere.Shared.ReserveInput i,
        CancellationToken ct
    )
    {
        if (Unavailable)
            throw new HttpRequestException("offline");
        if (OutOfStock)
            throw new ShopSphere.Shared.DomainException(409, "Out of stock");
        Reservations++;
        return Task.FromResult(
            new ShopSphere.Shared.ReservationDto(
                i.OrderId,
                i.Items.Select(x => new ShopSphere.Shared.PurchasedItem(
                        x.ProductId,
                        "Snapshot name",
                        12.5m,
                        x.Quantity
                    ))
                    .ToList()
            )
        );
    }

    public Task Release(Guid id, CancellationToken ct)
    {
        Releases++;
        return Task.CompletedTask;
    }
}

public class FakeNotifications : INotificationGateway
{
    public bool Unavailable { get; set; }
    public int Sent { get; set; }

    public Task Send(ShopSphere.Shared.NotificationInput i, CancellationToken ct)
    {
        if (Unavailable)
            throw new HttpRequestException("offline");
        Sent++;
        return Task.CompletedTask;
    }
}

public class OrderFactory(IProductGateway? gateway = null, INotificationGateway? notify = null)
    : WebApplicationFactory<order::Program>
{
    readonly SqliteConnection connection = Open();

    static SqliteConnection Open()
    {
        var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        return c;
    }

    public FakeProducts Products { get; } = new();
    public FakeNotifications Notifications { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        TestConfig.Configure(b);
        b.ConfigureServices(s =>
        {
            TestConfig.Database<OrderDb>(s, connection);
            s.RemoveAll<IProductGateway>();
            s.RemoveAll<INotificationGateway>();
            s.AddSingleton(gateway ?? Products);
            s.AddSingleton(notify ?? Notifications);
        });
    }

    public HttpClient Start()
    {
        var c = CreateClient();
        TestConfig.Init<OrderDb>(Services);
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            connection.Dispose();
    }
}

public class WebFactory(
    ProductFactory products,
    IdentityFactory identities,
    OrderFactory orders,
    NotificationFactory notifications
) : WebApplicationFactory<web::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        TestConfig.Configure(b);
        b.ConfigureServices(s =>
        {
            s.AddHttpClient("Product")
                .ConfigurePrimaryHttpMessageHandler(() => products.Server.CreateHandler());
            s.AddHttpClient("Identity")
                .ConfigurePrimaryHttpMessageHandler(() => identities.Server.CreateHandler());
            s.AddHttpClient("Order")
                .ConfigurePrimaryHttpMessageHandler(() => orders.Server.CreateHandler());
            s.AddHttpClient("Notification")
                .ConfigurePrimaryHttpMessageHandler(() => notifications.Server.CreateHandler());
        });
    }
}
