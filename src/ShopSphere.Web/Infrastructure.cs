using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace ShopSphere.Web;

public class TokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var context = accessor.HttpContext;
        if (context != null)
        {
            var token = await context.GetTokenAsync("access_token");
            if (token != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, ct);
    }
}

public class ApiClient(IHttpClientFactory clients)
{
    public async Task<T> Get<T>(string service, string path)
    {
        using var response = await clients.CreateClient(service).GetAsync(path);
        await Check(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<T> Post<T>(string service, string path, object input)
    {
        using var response = await clients.CreateClient(service).PostAsJsonAsync(path, input);
        await Check(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task Send(string service, HttpMethod method, string path, object? input = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (input != null)
            request.Content = JsonContent.Create(input);
        using var response = await clients.CreateClient(service).SendAsync(request);
        await Check(response);
    }

    static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var text = "The service could not complete your request.";
        try
        {
            var p = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            text = p?.Title ?? text;
            if (
                p?.Extensions.TryGetValue("errors", out var errors) == true
                && errors is JsonElement e
            )
                text = string.Join(
                    " ",
                    e.EnumerateObject()
                        .SelectMany(p => p.Value.EnumerateArray().Select(x => x.GetString()))
                );
        }
        catch (JsonException) { }
        throw new DomainException((int)response.StatusCode, text);
    }
}

public class CartStore(IHttpContextAccessor accessor)
{
    ISession Session => accessor.HttpContext!.Session;
    public List<OrderLineInput> Items =>
        JsonSerializer.Deserialize<List<OrderLineInput>>(Session.GetString("cart") ?? "[]")!;

    public void Save(List<OrderLineInput> items)
    {
        Session.SetString("cart", JsonSerializer.Serialize(items));
        Session.Remove("checkout");
    }

    public Guid RequestId
    {
        get
        {
            if (!Guid.TryParse(Session.GetString("checkout"), out var id))
            {
                id = Guid.NewGuid();
                Session.SetString("checkout", id.ToString());
            }
            return id;
        }
    }

    public void Clear()
    {
        Session.Remove("cart");
        Session.Remove("checkout");
    }
}

public record CartLine(ProductDto Product, int Quantity);

public class CartViewModel
{
    public List<CartLine> Lines { get; set; } = [];
    public decimal Total => Lines.Sum(x => x.Product.Price * x.Quantity);
    public PlaceOrderInput Checkout { get; set; } = new();
}

public class CatalogViewModel
{
    public Page<ProductDto> Products { get; set; } = new([], 1, 12, 0);
    public List<CategoryDto> Categories { get; set; } = [];
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
}

public class EditProductViewModel
{
    public int? Id { get; set; }
    public ProductInput Product { get; set; } = new();
    public List<CategoryDto> Categories { get; set; } = [];
}
