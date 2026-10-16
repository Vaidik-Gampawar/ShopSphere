using System.Data;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

namespace ProductService;

public class ProductLogic(ProductDb db)
{
    public static ProductDto Dto(Product p) =>
        new(
            p.Id,
            p.Name,
            p.Description,
            p.Price,
            p.Stock,
            p.CategoryId,
            p.Category.Name,
            p.IsActive
        );

    public async Task<Page<ProductDto>> List(
        string? search,
        int? category,
        int page,
        int size,
        bool includeInactive,
        CancellationToken ct
    )
    {
        if (page < 1 || page > 1000000 || size < 1 || size > 100)
            throw new DomainException(
                400,
                "Page must be positive and pageSize must be between 1 and 100."
            );
        var q = db
            .Products.Include(p => p.Category)
            .AsNoTracking()
            .Where(p => includeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.Name.Contains(search) || p.Description.Contains(search));
        if (category.HasValue)
            q = q.Where(p => p.CategoryId == category);
        var count = await q.CountAsync(ct);
        var products = await q.OrderBy(p => p.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new(products.Select(Dto).ToList(), page, size, count);
    }

    public async Task<ProductDto> Get(int id, bool admin, CancellationToken ct) =>
        Dto(
            await db
                .Products.Include(p => p.Category)
                .SingleOrDefaultAsync(p => p.Id == id && (admin || p.IsActive), ct)
                ?? throw new DomainException(404, "Product not found.")
        );

    public async Task<ProductDto> Save(int? id, ProductInput input, CancellationToken ct)
    {
        var category =
            await db.Categories.FindAsync([input.CategoryId], ct)
            ?? throw new DomainException(400, "Category does not exist.");
        // Stock is adjusted atomically; an administrator's edits must not overwrite concurrent reservations.
        if (id.HasValue)
        {
            var changed = await db
                .Products.Where(p => p.Id == id)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(p => p.Name, input.Name.Trim())
                            .SetProperty(p => p.Description, input.Description.Trim())
                            .SetProperty(p => p.Price, input.Price)
                            .SetProperty(p => p.CategoryId, input.CategoryId)
                            .SetProperty(p => p.IsActive, input.IsActive),
                    ct
                );
            if (changed == 0)
                throw new DomainException(404, "Product not found.");
            return await Get(id.Value, true, ct);
        }
        var p = new Product
        {
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Price = input.Price,
            Stock = input.Stock,
            Category = category,
            IsActive = input.IsActive,
        };
        db.Products.Add(p);
        await db.SaveChangesAsync(ct);
        return Dto(p);
    }

    public async Task AdjustStock(int id, int delta, CancellationToken ct)
    {
        if (delta == 0 || delta < -1000000 || delta > 1000000)
            throw new DomainException(
                400,
                "Stock adjustment must be nonzero and within one million units."
            );
        var rows = await db
            .Products.Where(p => p.Id == id && p.Stock + delta >= 0 && p.Stock + delta <= 1000000)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + delta), ct);
        if (rows == 0)
            throw new DomainException(
                409,
                "Product missing or stock adjustment exceeds inventory limits."
            );
    }

    public async Task<ReservationDto> Reserve(ReserveInput input, CancellationToken ct)
    {
        if (
            input.OrderId == Guid.Empty
            || input.Items is not { Count: > 0 and <= 50 }
            || input.Items.Any(i => i.Quantity < 1 || i.Quantity > 99 || i.ProductId < 1)
            || input.Items.Select(i => i.ProductId).Distinct().Count() != input.Items.Count
        )
            throw new DomainException(
                400,
                "Provide a request ID and 1–50 distinct products with quantities from 1 to 99."
            );
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        var existing = await db
            .Reservations.Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.Id == input.OrderId, ct);
        if (existing != null)
        {
            if (existing.Released)
                throw new DomainException(
                    409,
                    "This reservation was released. Use a new request ID."
                );
            if (
                existing.Items.Count != input.Items.Count
                || existing.Items.Any(i =>
                    !input.Items.Any(x => x.ProductId == i.ProductId && x.Quantity == i.Quantity)
                )
            )
                throw new DomainException(409, "Request ID already used with different items.");
            return new(
                existing.Id,
                existing
                    .Items.Select(i => new PurchasedItem(
                        i.ProductId,
                        i.ProductName,
                        i.UnitPrice,
                        i.Quantity
                    ))
                    .ToList()
            );
        }
        var reservation = new Reservation { Id = input.OrderId };
        foreach (var item in input.Items.OrderBy(i => i.ProductId))
        {
            var p =
                await db.Products.SingleOrDefaultAsync(
                    p => p.Id == item.ProductId && p.IsActive,
                    ct
                ) ?? throw new DomainException(400, $"Product {item.ProductId} is unavailable.");
            if (p.Stock < item.Quantity)
                throw new DomainException(409, $"Insufficient inventory for {p.Name}.");
            p.Stock -= item.Quantity;
            reservation.Items.Add(
                new()
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    UnitPrice = p.Price,
                    Quantity = item.Quantity,
                }
            );
        }
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(
            reservation.Id,
            reservation
                .Items.Select(i => new PurchasedItem(
                    i.ProductId,
                    i.ProductName,
                    i.UnitPrice,
                    i.Quantity
                ))
                .ToList()
        );
    }

    public async Task Release(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        var r = await db
            .Reservations.Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (r == null || r.Released)
            return;
        foreach (var i in r.Items)
            await db
                .Products.Where(p => p.Id == i.ProductId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.Stock, p => p.Stock + i.Quantity),
                    ct
                );
        r.Released = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
