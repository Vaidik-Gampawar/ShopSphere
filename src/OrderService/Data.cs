using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

namespace OrderService;

public class Order
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "PendingInventory";
    public bool NotificationSent { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderDb(DbContextOptions<OrderDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>(e =>
        {
            e.Property(o => o.UserId).HasMaxLength(36);
            e.Property(o => o.Email).HasMaxLength(254);
            e.Property(o => o.ShippingAddress).HasMaxLength(500);
            e.Property(o => o.Status).HasMaxLength(30);
            e.HasIndex(o => new { o.UserId, o.CreatedUtc });
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
        });
        m.Entity<OrderItem>(e =>
        {
            e.Property(i => i.ProductName).HasMaxLength(120);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.ToTable(t => t.HasCheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0"));
        });
    }

    public static OrderDto Dto(Order o) =>
        new(
            o.Id,
            o.UserId,
            o.CreatedUtc,
            o.Status,
            o.ShippingAddress,
            o.Items.Sum(i => i.UnitPrice * i.Quantity),
            o.Items.Select(i => new PurchasedItem(
                    i.ProductId,
                    i.ProductName,
                    i.UnitPrice,
                    i.Quantity
                ))
                .ToList(),
            o.NotificationSent
        );
}
