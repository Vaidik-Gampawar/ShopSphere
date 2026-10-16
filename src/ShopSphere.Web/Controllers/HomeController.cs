using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace ShopSphere.Web.Controllers;

public class HomeController(ApiClient api) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await api.Get<Page<ProductDto>>("Product", "/api/products?pageSize=3"));

    [Authorize]
    public async Task<IActionResult> Dashboard() =>
        View(await api.Get<Page<OrderDto>>("Order", "/api/orders?pageSize=5"));

    public IActionResult Error()
    {
        Response.StatusCode = 503;
        return View();
    }

    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View();
    }
}

public class CatalogController(ApiClient api) : Controller
{
    public async Task<IActionResult> Index(string? search, int? categoryId, int page = 1)
    {
        var products = await api.Get<Page<ProductDto>>(
            "Product",
            $"/api/products?search={Uri.EscapeDataString(search ?? "")}&categoryId={categoryId}&page={page}&pageSize=12"
        );
        return View(
            new CatalogViewModel
            {
                Products = products,
                Categories = await api.Get<List<CategoryDto>>("Product", "/api/categories"),
                Search = search,
                CategoryId = categoryId,
            }
        );
    }

    public async Task<IActionResult> Details(int id)
    {
        try
        {
            return View(await api.Get<ProductDto>("Product", $"/api/products/{id}"));
        }
        catch (DomainException ex) when (ex.Status == 404)
        {
            return NotFound();
        }
    }
}
