using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())
);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<TokenHandler>();
foreach (var name in new[] { "Product", "Order", "Identity", "Notification" })
    builder
        .Services.AddHttpClient(
            name,
            c =>
            {
                c.BaseAddress = new Uri(
                    builder.Configuration[$"Services:{name}"]
                        ?? throw new InvalidOperationException($"Missing Services:{name}")
                );
                c.Timeout = TimeSpan.FromSeconds(25);
            }
        )
        .AddHttpMessageHandler<TokenHandler>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<CartStore>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.IdleTimeout = TimeSpan.FromHours(2);
});
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Home/Denied";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        o.SlidingExpiration = false;
    });
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
await app.RunAsync();

public partial class Program;
