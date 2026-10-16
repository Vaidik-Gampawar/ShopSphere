using Microsoft.EntityFrameworkCore;
using OrderService;
using ShopSphere.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddDbContext<OrderDb>(o =>
    o.UseSqlServer(builder.Configuration.Required("ConnectionStrings:Database"))
);
builder.Services.AddHttpClient<IProductGateway, ProductGateway>(c =>
    c.ConfigureServiceClient(builder.Configuration, "Product")
);
builder.Services.AddHttpClient<INotificationGateway, NotificationGateway>(c =>
    c.ConfigureServiceClient(builder.Configuration, "Notification")
);
builder.Services.AddScoped<OrderLogic>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<OrderRecovery>();
var app = builder.Build();
app.UseServiceDefaults();
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrderDb>().Database.MigrateAsync();
}
await app.RunAsync();

public partial class Program;
