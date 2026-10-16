using System.ComponentModel.DataAnnotations;
using IdentityService;
using Microsoft.EntityFrameworkCore;
using OrderService;
using ProductService;
using ShopSphere.Shared;

namespace ShopSphere.Tests;

public class ValidationAndSchemaTests
{
    [Theory]
    [InlineData("weak", false)]
    [InlineData("Long-but-no-number!", false)]
    [InlineData("Strong-example-123!", true)]
    public void Password_policy_requires_complexity(string password, bool expected) =>
        Assert.Equal(expected, AuthLogic.StrongPassword(password));

    [Fact]
    public void Order_validation_rejects_empty_request_and_invalid_quantity()
    {
        var input = OrderTests.Input();
        input.RequestId = Guid.Empty;
        Assert.Equal(400, Assert.Throws<DomainException>(() => OrderLogic.Validate(input)).Status);
        input.RequestId = Guid.NewGuid();
        input.Items[0].Quantity = 100;
        Assert.Throws<DomainException>(() => OrderLogic.Validate(input));
    }

    [Theory]
    [InlineData("1.235", false)]
    [InlineData("1.23", true)]
    [InlineData("-1", false)]
    public void Product_prices_validate_precision_and_range(string value, bool expected)
    {
        var input = new ProductInput
        {
            Name = "Product",
            Description = "Description",
            Price = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            CategoryId = 1,
        };
        Assert.Equal(
            expected,
            Validator.TryValidateObject(input, new ValidationContext(input), [], true)
        );
    }

    [Fact]
    public void SqlServer_migrations_match_all_three_models_and_generate_scripts()
    {
        const string cs = "Server=unused;Database=unused;Integrated Security=True";
        using var p = new ProductDb(
            new DbContextOptionsBuilder<ProductDb>().UseSqlServer(cs).Options
        );
        using var o = new OrderDb(new DbContextOptionsBuilder<OrderDb>().UseSqlServer(cs).Options);
        using var i = new IdentityDb(
            new DbContextOptionsBuilder<IdentityDb>().UseSqlServer(cs).Options
        );
        foreach (var db in new DbContext[] { p, o, i })
        {
            Assert.NotEmpty(db.Database.GetMigrations());
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("CREATE TABLE", db.Database.GenerateCreateScript());
        }
    }
}
