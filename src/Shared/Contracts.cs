using System.ComponentModel.DataAnnotations;

namespace ShopSphere.Shared;

public record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public record CategoryDto(int Id, string Name);

public class CategoryInput
{
    [Required, StringLength(80, MinimumLength = 2)]
    public string Name { get; set; } = "";
}

public record ProductDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string Category,
    bool IsActive
);

public class ProductInput : IValidatableObject
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Required, StringLength(2000)]
    public string Description { get; set; } = "";

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Price { get; set; }

    [Range(0, 1000000)]
    public int Stock { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Price * 100 != decimal.Truncate(Price * 100))
            yield return new ValidationResult(
                "Price must have at most two decimal places.",
                [nameof(Price)]
            );
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
            yield return new ValidationResult(
                "Name must contain at least two non-space characters.",
                [nameof(Name)]
            );
    }
}

public class LoginInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}

public class RegisterInput : LoginInput
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = "";
}

public record AuthDto(
    string Token,
    DateTime ExpiresUtc,
    string UserId,
    string Name,
    string Email,
    string Role
);

public class OrderLineInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, 99)]
    public int Quantity { get; set; }
}

public class PlaceOrderInput
{
    public Guid RequestId { get; set; }

    [Required, StringLength(500, MinimumLength = 10)]
    public string ShippingAddress { get; set; } = "";

    [Required, MinLength(1), MaxLength(50)]
    public List<OrderLineInput> Items { get; set; } = [];
}

public record ReserveInput(Guid OrderId, List<OrderLineInput> Items);

public record PurchasedItem(int ProductId, string ProductName, decimal UnitPrice, int Quantity);

public record ReservationDto(Guid OrderId, IReadOnlyList<PurchasedItem> Items);

public record OrderDto(
    Guid Id,
    string UserId,
    DateTime CreatedUtc,
    string Status,
    string ShippingAddress,
    decimal Total,
    IReadOnlyList<PurchasedItem> Items,
    bool NotificationSent
);

public class StatusInput
{
    [Required, RegularExpression("^(Processing|Shipped|Delivered|Cancelled)$")]
    public string Status { get; set; } = "";
}

public record NotificationInput(Guid OrderId, string UserId, string Email, decimal Total);

public record NotificationDto(
    Guid Id,
    Guid OrderId,
    string UserId,
    string Email,
    string Message,
    DateTime CreatedUtc
);
