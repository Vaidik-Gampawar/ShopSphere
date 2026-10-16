using System.Threading.RateLimiting;
using IdentityService;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddDbContext<IdentityDb>(o =>
    o.UseSqlServer(builder.Configuration.Required("ConnectionStrings:Database"))
);
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<AuthLogic>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy(
        "auth",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );
});
var app = builder.Build();
app.UseRateLimiter();
app.UseServiceDefaults();
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDb>().Database.MigrateAsync();
}
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:Seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>();
    foreach (var (key, role) in new[] { ("Admin", "Administrator"), ("Customer", "Customer") })
    {
        var email = app.Configuration.Required($"Seed:{key}:Email").Trim().ToUpperInvariant();
        var password = app.Configuration.Required($"Seed:{key}:Password");
        if (!AuthLogic.StrongPassword(password))
            throw new InvalidOperationException(
                $"Seed:{key}:Password does not meet password policy."
            );
        if (!await db.Users.AnyAsync(u => u.Email == email))
        {
            var u = new UserAccount
            {
                Email = email,
                Name = $"Demo {key}",
                Role = role,
            };
            u.PasswordHash = hasher.HashPassword(u, password);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
    }
}
await app.RunAsync();

public partial class Program;
