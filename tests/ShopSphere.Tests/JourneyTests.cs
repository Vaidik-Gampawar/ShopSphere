using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using OrderService;
using ShopSphere.Shared;

namespace ShopSphere.Tests;

public class JourneyTests
{
    [Fact]
    public async Task All_services_and_MVC_complete_customer_checkout()
    {
        using var products = new ProductFactory();
        using var pc = products.Start();
        var productId = await products.Seed();
        pc.DefaultRequestHeaders.Add("X-Service-Key", TestConfig.ServiceKey);
        using var notifications = new NotificationFactory();
        using var nc = notifications.CreateClient();
        nc.DefaultRequestHeaders.Add("X-Service-Key", TestConfig.ServiceKey);
        using var identities = new IdentityFactory();
        using var ic = identities.Start();
        using var orders = new OrderFactory(new ProductGateway(pc), new NotificationGateway(nc));
        using var oc = orders.Start();
        using var web = new WebFactory(products, identities, orders, notifications);
        using var wc = web.CreateClient();
        var home = await wc.GetStringAsync("/");
        Assert.Contains("Good things.", home);
        Assert.Contains("Test headphones", home);
        var registration = await wc.GetStringAsync("/Account/Register");
        var register = await wc.PostAsync(
            "/Account/Register",
            Form(
                registration,
                new()
                {
                    ["Name"] = "Journey Customer",
                    ["Email"] = "journey@example.test",
                    ["Password"] = "Strong-journey-password-123!",
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Contains("Welcome back", await register.Content.ReadAsStringAsync());
        var detail = await wc.GetStringAsync($"/Catalog/Details/{productId}");
        var add = await wc.PostAsync(
            "/Cart/Add",
            Form(detail, new() { ["productId"] = productId.ToString(), ["quantity"] = "2" })
        );
        var cart = await add.Content.ReadAsStringAsync();
        Assert.Contains("Test headphones", cart);
        var requestId = Regex
            .Match(cart, "name=\"Checkout.RequestId\"[^>]*value=\"([^\"]+)\"")
            .Groups[1]
            .Value;
        Assert.False(string.IsNullOrEmpty(requestId));
        var checkout = await wc.PostAsync(
            "/Cart/Checkout",
            Form(
                cart,
                new()
                {
                    ["Checkout.RequestId"] = requestId,
                    ["Checkout.ShippingAddress"] = "123 Journey Street, Test City",
                }
            )
        );
        var orderPage = await checkout.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.Contains("Confirmed", orderPage);
        Assert.Contains("59.90", orderPage);
        Assert.Equal(
            8,
            (await pc.GetFromJsonAsync<ProductDto>($"/api/products/{productId}"))!.Stock
        );
        var history = await wc.GetStringAsync("/Orders");
        Assert.Contains("59.90", history);
        Assert.Contains("Thank you!", await wc.GetStringAsync("/Orders/Notifications"));
        var admin = await wc.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task MVC_rejects_missing_antiforgery_token()
    {
        using var p = new ProductFactory();
        using var pc = p.Start();
        using var i = new IdentityFactory();
        using var ic = i.Start();
        using var n = new NotificationFactory();
        using var nc = n.CreateClient();
        using var o = new OrderFactory();
        using var oc = o.Start();
        using var web = new WebFactory(p, i, o, n);
        using var c = web.CreateClient();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await c.PostAsync(
                    "/Account/Login",
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            { "Email", "test@example.test" },
                            { "Password", "irrelevant" },
                        }
                    )
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Administrator_can_manage_catalog_through_MVC_forms()
    {
        using var p = new ProductFactory();
        using var pc = p.Start();
        await p.Seed();
        using var i = new IdentityFactory();
        using var ic = i.Start();
        using (
            var scope =
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(
                    i.Services
                )
        )
        {
            var db =
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IdentityService.IdentityDb>(
                    scope.ServiceProvider
                );
            var user = new IdentityService.UserAccount
            {
                Email = "ADMIN@EXAMPLE.TEST",
                Name = "Test Admin",
                Role = "Administrator",
            };
            user.PasswordHash =
                new Microsoft.AspNetCore.Identity.PasswordHasher<IdentityService.UserAccount>().HashPassword(
                    user,
                    "Test-admin-password-123!"
                );
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        using var n = new NotificationFactory();
        using var nc = n.CreateClient();
        using var o = new OrderFactory();
        using var oc = o.Start();
        using var web = new WebFactory(p, i, o, n);
        using var c = web.CreateClient();
        var login = await c.GetStringAsync("/Account/Login");
        var signedIn = await c.PostAsync(
            "/Account/Login",
            Form(
                login,
                new()
                {
                    ["Email"] = "admin@example.test",
                    ["Password"] = "Test-admin-password-123!",
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Contains("Administrator dashboard", await c.GetStringAsync("/Admin"));
        var edit = await c.GetStringAsync("/Admin/Edit");
        var created = await c.PostAsync(
            "/Admin/Edit",
            Form(
                edit,
                new()
                {
                    ["Product.Name"] = "Admin created product",
                    ["Product.Description"] = "Created through the real MVC form",
                    ["Product.Price"] = "9.95",
                    ["Product.Stock"] = "3",
                    ["Product.CategoryId"] = "1",
                    ["Product.IsActive"] = "true",
                }
            )
        );
        Assert.Contains("Product saved.", await created.Content.ReadAsStringAsync());
        var catalog = await pc.GetFromJsonAsync<Page<ProductDto>>("/api/products?search=Admin");
        var product = Assert.Single(catalog!.Items);
        Assert.Equal(3, product.Stock);
        var products = await c.GetStringAsync("/Admin/Products");
        var adjusted = await c.PostAsync(
            "/Admin/Inventory",
            Form(products, new() { ["id"] = product.Id.ToString(), ["delta"] = "4" })
        );
        Assert.Contains("Inventory updated.", await adjusted.Content.ReadAsStringAsync());
        Assert.Equal(
            7,
            (await pc.GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}"))!.Stock
        );
        var categories = await c.GetStringAsync("/Admin/Categories");
        var category = await c.PostAsync(
            "/Admin/SaveCategory",
            Form(categories, new() { ["Name"] = "New category" })
        );
        Assert.Contains("Category saved.", await category.Content.ReadAsStringAsync());
        Assert.Contains(
            (await pc.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!,
            x => x.Name == "New category"
        );
    }

    static FormUrlEncodedContent Form(string html, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = WebUtility.HtmlDecode(
            Regex
                .Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
                .Groups[1]
                .Value
        );
        return new(values);
    }
}
