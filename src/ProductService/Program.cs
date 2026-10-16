using Microsoft.EntityFrameworkCore;
using ProductService;
using ShopSphere.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddDbContext<ProductDb>(o =>
    o.UseSqlServer(builder.Configuration.Required("ConnectionStrings:Database"))
);
builder.Services.AddScoped<ProductLogic>();
var app = builder.Build();
app.UseServiceDefaults();
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ProductDb>().Database.MigrateAsync();
}
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:Seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProductDb>();
    if (!await db.Categories.AnyAsync())
    {
        var desk = new Category { Name = "Workspace" };
        var tech = new Category { Name = "Technology" };
        var life = new Category { Name = "Everyday" };
        db.Products.AddRange(
            new Product
            {
                Name = "Studio headphones",
                Description =
                    "Comfortable over-ear headphones with a balanced sound and soft cushions.",
                Price = 129,
                Stock = 40,
                Category = tech,
            },
            new Product
            {
                Name = "Mechanical keyboard",
                Description =
                    "A compact keyboard with tactile switches and a durable aluminum frame.",
                Price = 89,
                Stock = 35,
                Category = tech,
            },
            new Product
            {
                Name = "Task lamp",
                Description = "Warm, adjustable lighting for a focused workspace.",
                Price = 49,
                Stock = 50,
                Category = desk,
            },
            new Product
            {
                Name = "Canvas weekender",
                Description = "A practical cotton canvas bag for your next short escape.",
                Price = 79,
                Stock = 25,
                Category = life,
            },
            new Product
            {
                Name = "Ceramic mug",
                Description = "A hand-finished ceramic mug for slow mornings.",
                Price = 18,
                Stock = 80,
                Category = life,
            },
            new Product
            {
                Name = "Desk organizer",
                Description = "Keep your essentials within reach with a solid oak organizer.",
                Price = 35,
                Stock = 45,
                Category = desk,
            }
        );
        await db.SaveChangesAsync();
    }
}
await app.RunAsync();

public partial class Program;
