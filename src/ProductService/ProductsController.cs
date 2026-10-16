using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Shared;

namespace ProductService;

[ApiController, Route("api/products")]
public class ProductsController(ProductLogic logic, ProductDb db) : ControllerBase
{
    [HttpGet]
    public Task<Page<ProductDto>> List(
        string? search,
        int? categoryId,
        int page = 1,
        int pageSize = 12,
        bool includeInactive = false,
        CancellationToken ct = default
    ) =>
        logic.List(
            search,
            categoryId,
            page,
            pageSize,
            includeInactive && User.IsInRole("Administrator"),
            ct
        );

    [HttpGet("{id:int}")]
    public Task<ProductDto> Get(int id, CancellationToken ct) =>
        logic.Get(id, User.IsInRole("Administrator"), ct);

    [HttpPost, Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ProductDto>> Create(ProductInput input, CancellationToken ct)
    {
        var p = await logic.Save(null, input, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Administrator")]
    public Task<ProductDto> Update(int id, ProductInput input, CancellationToken ct) =>
        logic.Save(id, input, ct);

    [HttpDelete("{id:int}"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        if (
            await db
                .Products.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false), ct) == 0
        )
            return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/inventory"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Inventory(int id, InventoryInput input, CancellationToken ct)
    {
        await logic.AdjustStock(id, input.Delta, ct);
        return NoContent();
    }
}

public record InventoryInput(int Delta);

[ApiController, Route("api/categories")]
public class CategoriesController(ProductDb db) : ControllerBase
{
    [HttpGet]
    public async Task<List<CategoryDto>> List(CancellationToken ct) =>
        await db
            .Categories.OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync(ct);

    [HttpPost, Authorize(Roles = "Administrator")]
    public async Task<ActionResult<CategoryDto>> Create(CategoryInput input, CancellationToken ct)
    {
        var name = input.Name.Trim();
        if (name.Length < 2)
            throw new DomainException(400, "Category name is too short.");
        if (await db.Categories.AnyAsync(c => c.Name == name, ct))
            throw new DomainException(409, "Category already exists.");
        var c = new Category { Name = name };
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return Created($"/api/categories/{c.Id}", new CategoryDto(c.Id, c.Name));
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Update(int id, CategoryInput input, CancellationToken ct)
    {
        var c =
            await db.Categories.FindAsync([id], ct)
            ?? throw new DomainException(404, "Category not found.");
        var name = input.Name.Trim();
        if (name.Length < 2)
            throw new DomainException(400, "Category name is too short.");
        if (await db.Categories.AnyAsync(c => c.Id != id && c.Name == name, ct))
            throw new DomainException(409, "Category already exists.");
        c.Name = name;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

[ApiController, Route("internal/reservations")]
public class ReservationsController(ProductLogic logic, IConfiguration config) : ControllerBase
{
    [HttpPost]
    public Task<ReservationDto> Reserve(ReserveInput input, CancellationToken ct)
    {
        Hosting.RequireService(HttpContext, config);
        return logic.Reserve(input, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
    {
        Hosting.RequireService(HttpContext, config);
        await logic.Release(id, ct);
        return NoContent();
    }
}
