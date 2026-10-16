using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderService;
using ShopSphere.Shared;

namespace ShopSphere.Tests;

public class OrderTests
{
    public static PlaceOrderInput Input() =>
        new()
        {
            RequestId = Guid.NewGuid(),
            ShippingAddress = "123 Test Street, Test City",
            Items = [new() { ProductId = 1, Quantity = 2 }],
        };

    [Fact]
    public async Task Order_snapshots_prices_and_retries_without_duplicates()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        var input = Input();
        var r = await c.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var order = (await r.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal(25m, order.Total);
        Assert.Equal("Snapshot name", order.Items[0].ProductName);
        Assert.True(order.NotificationSent);
        await c.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(1, f.Products.Reservations);
        Assert.Equal(1, f.Notifications.Sent);
        input.Items[0].Quantity = 3;
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync("/api/orders", input)).StatusCode
        );
    }

    [Fact]
    public async Task Invalid_orders_are_rejected_before_inventory_calls()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        var input = Input();
        input.Items.Add(new() { ProductId = 1, Quantity = 1 });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync("/api/orders", input)).StatusCode
        );
        Assert.Equal(0, f.Products.Reservations);
        input.Items = [];
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync("/api/orders", input)).StatusCode
        );
    }

    [Fact]
    public async Task Customer_cannot_read_another_customers_order_or_change_status()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        var input = Input();
        await c.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await c.PutAsJsonAsync(
                    $"/api/orders/{input.RequestId}/status",
                    new StatusInput { Status = "Processing" }
                )
            ).StatusCode
        );
        c.As(TestConfig.Token());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await c.GetAsync($"/api/orders/{input.RequestId}")).StatusCode
        );
        Assert.Empty((await c.GetFromJsonAsync<Page<OrderDto>>("/api/orders?all=true"))!.Items);
    }

    [Fact]
    public async Task Inventory_outage_is_persisted_then_recovered()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        f.Products.Unavailable = true;
        var input = Input();
        var r = await c.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        f.Products.Unavailable = false;
        using (var scope = f.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<OrderLogic>()
                .Advance(input.RequestId, default);
        Assert.Equal(
            "Confirmed",
            (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{input.RequestId}"))!.Status
        );
    }

    [Fact]
    public async Task Failed_notification_does_not_lose_order_and_is_retried()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        f.Notifications.Unavailable = true;
        var input = Input();
        var r = await c.PostAsJsonAsync("/api/orders", input);
        var order = (await r.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal("Confirmed", order.Status);
        Assert.False(order.NotificationSent);
        f.Notifications.Unavailable = false;
        using (var scope = f.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<OrderLogic>()
                .Advance(input.RequestId, default);
        Assert.Equal(1, f.Notifications.Sent);
        Assert.Equal(1, f.Products.Reservations);
    }

    [Fact]
    public async Task Insufficient_inventory_records_rejected_order()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        f.Products.OutOfStock = true;
        var r = await c.PostAsJsonAsync("/api/orders", Input());
        Assert.Equal("Rejected", (await r.Content.ReadFromJsonAsync<OrderDto>())!.Status);
        Assert.Equal(0, f.Notifications.Sent);
    }

    [Fact]
    public async Task Cancellation_releases_stock_and_invalid_transitions_fail()
    {
        using var f = new OrderFactory();
        using var c = f.Start().As(TestConfig.Token());
        var input = Input();
        await c.PostAsJsonAsync("/api/orders", input);
        c.As(TestConfig.Token("Administrator"));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await c.PutAsJsonAsync(
                    $"/api/orders/{input.RequestId}/status",
                    new StatusInput { Status = "Delivered" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await c.PutAsJsonAsync(
                    $"/api/orders/{input.RequestId}/status",
                    new StatusInput { Status = "Cancelled" }
                )
            ).StatusCode
        );
        Assert.Equal(1, f.Products.Releases);
        Assert.Equal(
            "Cancelled",
            (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{input.RequestId}"))!.Status
        );
    }
}
