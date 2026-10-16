using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

namespace OrderService;

public class OrderLogic(
    OrderDb db,
    IProductGateway products,
    INotificationGateway notifications,
    ILogger<OrderLogic> logger
)
{
    public static void Validate(PlaceOrderInput input)
    {
        if (
            input.RequestId == Guid.Empty
            || input.Items is not { Count: > 0 and <= 50 }
            || input.Items.Any(i => i.ProductId < 1 || i.Quantity < 1 || i.Quantity > 99)
            || input.Items.Select(i => i.ProductId).Distinct().Count() != input.Items.Count
            || string.IsNullOrWhiteSpace(input.ShippingAddress)
            || input.ShippingAddress.Trim().Length < 10
            || input.ShippingAddress.Length > 500
        )
            throw new DomainException(
                400,
                "Provide a request ID, shipping address, and 1–50 distinct products with quantities from 1 to 99."
            );
    }

    public async Task<OrderDto> Create(
        PlaceOrderInput input,
        string userId,
        string email,
        CancellationToken ct
    )
    {
        Validate(input);
        var existing = await db
            .Orders.Include(o => o.Items)
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == input.RequestId, ct);
        if (existing != null)
        {
            CheckReplay(existing, input, userId);
            return OrderDb.Dto(existing);
        }
        var order = new Order
        {
            Id = input.RequestId,
            UserId = userId,
            Email = email,
            ShippingAddress = input.ShippingAddress.Trim(),
            Items = input
                .Items.Select(i => new OrderItem { ProductId = i.ProductId, Quantity = i.Quantity })
                .ToList(),
        };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db
                .Orders.Include(o => o.Items)
                .AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == input.RequestId, ct);
            if (existing == null)
                throw;
            CheckReplay(existing, input, userId);
            return OrderDb.Dto(existing);
        }
        db.ChangeTracker.Clear();
        await Advance(order.Id, ct);
        return await Get(order.Id, userId, false, ct);
    }

    static void CheckReplay(Order order, PlaceOrderInput input, string userId)
    {
        if (
            order.UserId != userId
            || order.ShippingAddress != input.ShippingAddress.Trim()
            || order.Items.Count != input.Items.Count
            || order.Items.Any(i =>
                !input.Items.Any(x => x.ProductId == i.ProductId && x.Quantity == i.Quantity)
            )
        )
            throw new DomainException(409, "Request ID already used for a different order.");
    }

    public async Task<OrderDto> Get(Guid id, string userId, bool admin, CancellationToken ct)
    {
        var o =
            await db
                .Orders.AsNoTracking()
                .Include(o => o.Items)
                .SingleOrDefaultAsync(o => o.Id == id && (admin || o.UserId == userId), ct)
            ?? throw new DomainException(404, "Order not found.");
        return OrderDb.Dto(o);
    }

    public async Task<Page<OrderDto>> History(
        string userId,
        bool admin,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
            throw new DomainException(400, "Invalid pagination.");
        var q = db.Orders.AsNoTracking().Where(o => admin || o.UserId == userId);
        var count = await q.CountAsync(ct);
        var items = await q.Include(o => o.Items)
            .OrderByDescending(o => o.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new(items.Select(OrderDb.Dto).ToList(), page, pageSize, count);
    }

    public async Task SetStatus(Guid id, string status, CancellationToken ct)
    {
        var from = status switch
        {
            "Processing" => "Confirmed",
            "Shipped" => "Processing",
            "Delivered" => "Shipped",
            "Cancelled" => "Confirmed",
            _ => throw new DomainException(400, "Invalid order status."),
        };
        var target = status == "Cancelled" ? "CancelPending" : status;
        if (
            await db
                .Orders.Where(o => o.Id == id && o.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, target), ct) == 0
        )
            throw new DomainException(409, "Order missing or status transition is not allowed.");
        if (target == "CancelPending")
            await Advance(id, ct);
    }

    public async Task Advance(Guid id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now.AddMinutes(2);
        if (
            await db
                .Orders.Where(o => o.Id == id && (o.LeaseUntilUtc == null || o.LeaseUntilUtc < now))
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(o => o.LeaseUntilUtc, until)
                            .SetProperty(o => o.LastAttemptUtc, now),
                    ct
                ) == 0
        )
            return;
        try
        {
            db.ChangeTracker.Clear();
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == id, ct);
            if (order.Status == "PendingInventory")
            {
                ReservationDto reservation;
                try
                {
                    reservation = await products.Reserve(
                        new(
                            order.Id,
                            order
                                .Items.Select(i => new OrderLineInput
                                {
                                    ProductId = i.ProductId,
                                    Quantity = i.Quantity,
                                })
                                .ToList()
                        ),
                        ct
                    );
                }
                catch (DomainException e) when (e.Status is 400 or 404 or 409)
                {
                    order.Status = "Rejected";
                    await db.SaveChangesAsync(ct);
                    logger.LogWarning("Order {OrderId} rejected: {Reason}", id, e.Message);
                    return;
                }
                foreach (var item in order.Items)
                {
                    var p = reservation.Items.Single(p => p.ProductId == item.ProductId);
                    item.ProductName = p.ProductName;
                    item.UnitPrice = p.UnitPrice;
                }
                order.Status = "Confirmed";
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Order {OrderId} confirmed", id);
            }
            if (order.Status == "CancelPending")
            {
                await products.Release(order.Id, ct);
                order.Status = "Cancelled";
                await db.SaveChangesAsync(ct);
                return;
            }
            if (
                !order.NotificationSent
                && order.Status is "Confirmed" or "Processing" or "Shipped" or "Delivered"
            )
            {
                await notifications.Send(
                    new(
                        order.Id,
                        order.UserId,
                        order.Email,
                        order.Items.Sum(i => i.UnitPrice * i.Quantity)
                    ),
                    ct
                );
                order.NotificationSent = true;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or DomainException)
        {
            logger.LogWarning(ex, "Order {OrderId} is awaiting a service retry", id);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db
                .Orders.Where(o => o.Id == id && o.LeaseUntilUtc == until)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.LeaseUntilUtc, (DateTime?)null),
                    CancellationToken.None
                );
        }
    }
}

public class OrderRecovery(IServiceScopeFactory scopes, ILogger<OrderRecovery> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDb>();
                var logic = scope.ServiceProvider.GetRequiredService<OrderLogic>();
                var ids = await db
                    .Orders.Where(o =>
                        o.Status == "PendingInventory"
                        || o.Status == "CancelPending"
                        || (
                            !o.NotificationSent
                            && (
                                o.Status == "Confirmed"
                                || o.Status == "Processing"
                                || o.Status == "Shipped"
                                || o.Status == "Delivered"
                            )
                        )
                    )
                    .OrderBy(o => o.LastAttemptUtc)
                    .ThenBy(o => o.CreatedUtc)
                    .Select(o => o.Id)
                    .Take(50)
                    .ToListAsync(stoppingToken);
                foreach (var id in ids)
                    await logic.Advance(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Order recovery pass failed");
            }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
