using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

namespace NotificationService;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class NotificationDb(DbContextOptions<NotificationDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Notification>().HasIndex(n => n.OrderId).IsUnique();
        m.Entity<Notification>().Property(n => n.Email).HasMaxLength(254);
    }
}

[ApiController]
public class NotificationsController(
    NotificationDb db,
    IConfiguration config,
    ILogger<NotificationsController> logger
) : ControllerBase
{
    [HttpPost("internal/notifications")]
    public async Task<ActionResult<NotificationDto>> Create(
        NotificationInput input,
        CancellationToken ct
    )
    {
        Hosting.RequireService(HttpContext, config);
        if (
            input.OrderId == Guid.Empty
            || !Guid.TryParse(input.UserId, out _)
            || string.IsNullOrWhiteSpace(input.Email)
            || input.Total <= 0
        )
            throw new DomainException(400, "Invalid confirmation request.");
        var n = await db.Notifications.SingleOrDefaultAsync(n => n.OrderId == input.OrderId, ct);
        if (n == null)
        {
            n = new()
            {
                OrderId = input.OrderId,
                UserId = input.UserId,
                Email = input.Email,
                Message =
                    $"Thank you! Order {input.OrderId} is confirmed. Total: {input.Total:0.00} USD.",
            };
            db.Notifications.Add(n);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                n = await db.Notifications.SingleAsync(n => n.OrderId == input.OrderId, ct);
            }
            logger.LogInformation("Local confirmation recorded for order {OrderId}", input.OrderId);
        }
        return Ok(new NotificationDto(n.Id, n.OrderId, n.UserId, n.Email, n.Message, n.CreatedUtc));
    }

    [HttpGet("api/notifications"), Authorize]
    public async Task<List<NotificationDto>> List(
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default
    )
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
            throw new DomainException(400, "Invalid pagination.");
        var user = User.UserId();
        return await db
            .Notifications.AsNoTracking()
            .Where(n => n.UserId == user)
            .OrderByDescending(n => n.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.OrderId,
                n.UserId,
                n.Email,
                n.Message,
                n.CreatedUtc
            ))
            .ToListAsync(ct);
    }
}
