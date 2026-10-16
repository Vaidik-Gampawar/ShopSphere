using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace ShopSphere.Web.Controllers;

[Authorize]
public class OrdersController(ApiClient api) : Controller
{
    public async Task<IActionResult> Index(int page = 1) =>
        View(await api.Get<Page<OrderDto>>("Order", $"/api/orders?page={page}"));

    public async Task<IActionResult> Details(Guid id)
    {
        try
        {
            return View(await api.Get<OrderDto>("Order", $"/api/orders/{id}"));
        }
        catch (DomainException e) when (e.Status == 404)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> Notifications() =>
        View(await api.Get<List<NotificationDto>>("Notification", "/api/notifications"));
}

[Authorize(Roles = "Administrator")]
public class AdminController(ApiClient api) : Controller
{
    public async Task<IActionResult> Index(int page = 1) =>
        View(
            await api.Get<Page<OrderDto>>("Order", $"/api/orders?all=true&pageSize=20&page={page}")
        );

    public async Task<IActionResult> Products(int page = 1) =>
        View(
            await api.Get<Page<ProductDto>>(
                "Product",
                $"/api/products?includeInactive=true&page={page}"
            )
        );

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        var input = new ProductInput();
        if (id.HasValue)
        {
            var p = await api.Get<ProductDto>("Product", $"/api/products/{id}");
            input = new()
            {
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                CategoryId = p.CategoryId,
                IsActive = p.IsActive,
            };
        }
        return View(
            new EditProductViewModel
            {
                Id = id,
                Product = input,
                Categories = await api.Get<List<CategoryDto>>("Product", "/api/categories"),
            }
        );
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditProductViewModel model)
    {
        if (ModelState.IsValid)
            try
            {
                if (model.Id.HasValue)
                    await api.Send(
                        "Product",
                        HttpMethod.Put,
                        $"/api/products/{model.Id}",
                        model.Product
                    );
                else
                    await api.Post<ProductDto>("Product", "/api/products", model.Product);
                TempData["Success"] = "Product saved.";
                return RedirectToAction("Products");
            }
            catch (DomainException e)
            {
                ModelState.AddModelError("", e.Message);
            }
        model.Categories = await api.Get<List<CategoryDto>>("Product", "/api/categories");
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id)
    {
        await api.Send("Product", HttpMethod.Delete, $"/api/products/{id}");
        TempData["Success"] = "Product deactivated.";
        return RedirectToAction("Products");
    }

    [HttpPost]
    public async Task<IActionResult> Inventory(int id, int delta)
    {
        try
        {
            await api.Send(
                "Product",
                HttpMethod.Post,
                $"/api/products/{id}/inventory",
                new { delta }
            );
            TempData["Success"] = "Inventory updated.";
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }
        return RedirectToAction("Products");
    }

    [HttpPost]
    public async Task<IActionResult> Status(Guid id, string status)
    {
        try
        {
            await api.Send(
                "Order",
                HttpMethod.Put,
                $"/api/orders/{id}/status",
                new StatusInput { Status = status }
            );
            TempData["Success"] = "Order status updated.";
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> Categories() =>
        View(await api.Get<List<CategoryDto>>("Product", "/api/categories"));

    [HttpPost]
    public async Task<IActionResult> SaveCategory(int? id, CategoryInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a category name with 2–80 characters.";
            return RedirectToAction("Categories");
        }
        try
        {
            if (id.HasValue)
                await api.Send("Product", HttpMethod.Put, $"/api/categories/{id}", input);
            else
                await api.Post<CategoryDto>("Product", "/api/categories", input);
            TempData["Success"] = "Category saved.";
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }
        return RedirectToAction("Categories");
    }
}
