using Microsoft.EntityFrameworkCore;

namespace ProductService;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class Reservation
{
    public Guid Id { get; set; }
    public bool Released { get; set; }
    public List<ReservationItem> Items { get; set; } = [];
}

public class ReservationItem
{
    public int Id { get; set; }
    public Guid ReservationId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class ProductDb(DbContextOptions<ProductDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Category>().Property(x => x.Name).HasMaxLength(80);
        m.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        m.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Category).WithMany(x => x.Products).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Product_Stock", "[Stock] >= 0");
                t.HasCheckConstraint("CK_Product_Price", "[Price] > 0");
            });
        });
        m.Entity<Reservation>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReservationId);
        m.Entity<ReservationItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        m.Entity<ReservationItem>().Property(x => x.ProductName).HasMaxLength(120);
    }
}
