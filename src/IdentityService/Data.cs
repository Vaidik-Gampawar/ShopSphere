using Microsoft.EntityFrameworkCore;

namespace IdentityService;

public class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Customer";
}

public class IdentityDb(DbContextOptions<IdentityDb> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<UserAccount>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(254);
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.Role).HasMaxLength(30);
        });
    }
}
