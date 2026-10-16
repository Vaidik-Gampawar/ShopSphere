using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductService;
using ShopSphere.Shared;

namespace ShopSphere.Tests;

public class ProductTests
{
    [Fact]
    public async Task Catalog_filters_searches_and_paginates()
    {
        using var f = new ProductFactory();
        using var c = f.Start();
        await f.Seed();
        var page = await c.GetFromJsonAsync<Page<ProductDto>>(
            "/api/products?search=head&pageSize=1"
        );
        Assert.Equal(1, page!.TotalCount);
        Assert.Single(page.Items);
        Assert.Empty(
            (await c.GetFromJsonAsync<Page<ProductDto>>("/api/products?search=missing"))!.Items
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.GetAsync("/api/products?pageSize=101")).StatusCode
        );
    }

    [Fact]
    public async Task Administrative_writes_require_admin()
    {
        using var f = new ProductFactory();
        using var c = f.Start();
        var id = await f.Seed();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await c.DeleteAsync($"/api/products/{id}")).StatusCode
        );
        c.As(TestConfig.Token());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.DeleteAsync($"/api/products/{id}")).StatusCode
        );
        c.As(TestConfig.Token("Administrator"));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await c.DeleteAsync($"/api/products/{id}")).StatusCode
        );
        c.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/products/{id}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_product_returns_validation_problem()
    {
        using var f = new ProductFactory();
        using var c = f.Start().As(TestConfig.Token("Administrator"));
        var r = await c.PostAsJsonAsync(
            "/api/products",
            new ProductInput { Price = -1, Stock = -1 }
        );
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("errors", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_update_and_inventory_preserve_stock()
    {
        using var f = new ProductFactory();
        using var c = f.Start().As(TestConfig.Token("Administrator"));
        await f.Seed();
        var input = new ProductInput
        {
            Name = "New product",
            Description = "Description",
            Price = 15,
            Stock = 5,
            CategoryId = 1,
        };
        var result = await c.PostAsJsonAsync("/api/products", input);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var p = (await result.Content.ReadFromJsonAsync<ProductDto>())!;
        input.Stock = 999;
        input.Price = 20;
        Assert.Equal(
            HttpStatusCode.OK,
            (await c.PutAsJsonAsync($"/api/products/{p.Id}", input)).StatusCode
        );
        Assert.Equal(5, (await c.GetFromJsonAsync<ProductDto>($"/api/products/{p.Id}"))!.Stock);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await c.PostAsJsonAsync($"/api/products/{p.Id}/inventory", new { delta = -2 })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await c.PostAsJsonAsync($"/api/products/{p.Id}/inventory", new { delta = -10 })
            ).StatusCode
        );
    }

    [Fact]
    public async Task Reservations_are_atomic_idempotent_and_releasable()
    {
        using var f = new ProductFactory();
        using var c = f.Start();
        var id = await f.Seed(4);
        c.DefaultRequestHeaders.Add("X-Service-Key", TestConfig.ServiceKey);
        var input = new ReserveInput(Guid.NewGuid(), [new() { ProductId = id, Quantity = 3 }]);
        Assert.Equal(
            HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/internal/reservations", input)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/internal/reservations", input)).StatusCode
        );
        Assert.Equal(1, (await c.GetFromJsonAsync<ProductDto>($"/api/products/{id}"))!.Stock);
        var fail = new ReserveInput(
            Guid.NewGuid(),
            [new() { ProductId = id, Quantity = 1 }, new() { ProductId = 999, Quantity = 1 }]
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync("/internal/reservations", fail)).StatusCode
        );
        Assert.Equal(1, (await c.GetFromJsonAsync<ProductDto>($"/api/products/{id}"))!.Stock);
        await c.DeleteAsync($"/internal/reservations/{input.OrderId}");
        await c.DeleteAsync($"/internal/reservations/{input.OrderId}");
        Assert.Equal(4, (await c.GetFromJsonAsync<ProductDto>($"/api/products/{id}"))!.Stock);
    }

    [Fact]
    public async Task Internal_endpoints_reject_missing_key()
    {
        using var f = new ProductFactory();
        using var c = f.Start();
        var r = await c.PostAsJsonAsync(
            "/internal/reservations",
            new ReserveInput(Guid.NewGuid(), [new() { ProductId = 1, Quantity = 1 }])
        );
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("traceId", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public void SqlServer_model_contains_constraints_and_migration()
    {
        using var db = new ProductDb(
            new DbContextOptionsBuilder<ProductDb>()
                .UseSqlServer("Server=unused;Database=unused")
                .Options
        );
        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.Contains("CK_Product_Stock", db.Database.GenerateCreateScript());
    }
}
