using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace ShopSphere.Web.Controllers;

[Authorize]
public class CartController(ApiClient api, CartStore cart) : Controller
{
    async Task<CartViewModel> Model(PlaceOrderInput? input = null)
    {
        var lines = new List<CartLine>();
        foreach (var i in cart.Items)
            lines.Add(
                new(
                    await api.Get<ProductDto>("Product", $"/api/products/{i.ProductId}"),
                    i.Quantity
                )
            );
        return new() { Lines = lines, Checkout = input ?? new() { RequestId = cart.RequestId } };
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            return View(await Model());
        }
        catch (DomainException)
        {
            TempData["Error"] =
                "A cart product is no longer available. Clear the cart to continue.";
            return View(new CartViewModel());
        }
    }

    [HttpPost]
    public async Task<IActionResult> Add(int productId, int quantity = 1)
    {
        if (quantity < 1 || quantity > 99)
        {
            TempData["Error"] = "Choose a quantity from 1 to 99.";
            return RedirectToAction("Index");
        }
        try
        {
            var p = await api.Get<ProductDto>("Product", $"/api/products/{productId}");
            var items = cart.Items;
            var line = items.SingleOrDefault(i => i.ProductId == productId);
            var total = (line?.Quantity ?? 0) + quantity;
            if (total > p.Stock || total > 99 || items.Count >= 50 && line == null)
                throw new DomainException(
                    409,
                    "Requested quantity is unavailable or cart limit was reached."
                );
            if (line == null)
                items.Add(new() { ProductId = productId, Quantity = quantity });
            else
                line.Quantity = total;
            cart.Save(items);
            TempData["Success"] = "Added to your cart.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Remove(int productId)
    {
        cart.Save(cart.Items.Where(i => i.ProductId != productId).ToList());
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Update(int productId, int quantity)
    {
        var items = cart.Items;
        var line = items.SingleOrDefault(i => i.ProductId == productId);
        if (line != null && quantity is >= 1 and <= 99)
        {
            line.Quantity = quantity;
            cart.Save(items);
        }
        else
            TempData["Error"] = "Choose a quantity from 1 to 99.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Clear()
    {
        cart.Clear();
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Checkout([Bind(Prefix = "Checkout")] PlaceOrderInput input)
    {
        input.Items = cart.Items;
        ModelState.Remove("Items");
        ModelState.Remove("Checkout.Items");
        if (input.RequestId != cart.RequestId)
        {
            TempData["Error"] = "Your cart changed. Review it before checking out.";
            return RedirectToAction("Index");
        }
        if (input.Items.Count == 0)
        {
            TempData["Error"] = "Your cart is empty.";
            return RedirectToAction("Index");
        }
        if (!ModelState.IsValid)
            return View("Index", await Model(input));
        try
        {
            var order = await api.Post<OrderDto>("Order", "/api/orders", input);
            if (order.Status == "Rejected")
            {
                TempData["Error"] = "Inventory changed. Review your cart and try again.";
                cart.Save(cart.Items);
                return RedirectToAction("Index");
            }
            cart.Clear();
            TempData["Success"] =
                order.Status == "PendingInventory"
                    ? "Order received. Inventory confirmation is pending."
                    : "Your order is confirmed.";
            return RedirectToAction("Details", "Orders", new { id = order.Id });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View("Index", await Model(input));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                "",
                "Could not confirm the result. Retry with this cart to safely check the same order."
            );
            return View("Index", await Model(input));
        }
    }
}
