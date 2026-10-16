using Microsoft.EntityFrameworkCore;
using NotificationService;
using ShopSphere.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddDbContext<NotificationDb>(o =>
    o.UseSqlite(
        builder.Configuration.GetConnectionString("Notifications") ?? "Data Source=notifications.db"
    )
);
var app = builder.Build();
app.UseServiceDefaults();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<NotificationDb>().Database.EnsureCreatedAsync();
await app.RunAsync();

public partial class Program;
