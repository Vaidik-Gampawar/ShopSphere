using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace ShopSphere.Web.Controllers;

public class AccountController(ApiClient api, CartStore cart) : Controller
{
    [HttpGet]
    public IActionResult Login() => View(new LoginInput());

    [HttpGet]
    public IActionResult Register() => View(new RegisterInput());

    [HttpPost]
    public async Task<IActionResult> Login(LoginInput input, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(input);
        try
        {
            await SignIn(await api.Post<AuthDto>("Identity", "/api/auth/login", input));
            return Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl!)
                : RedirectToAction("Dashboard", "Home");
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(input);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "Sign in is temporarily unavailable. Please retry.");
            return View(input);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid)
            return View(input);
        try
        {
            await SignIn(await api.Post<AuthDto>("Identity", "/api/auth/register", input));
            return RedirectToAction("Dashboard", "Home");
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(input);
        }
    }

    async Task SignIn(AuthDto auth)
    {
        cart.Clear();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, auth.UserId),
            new Claim(ClaimTypes.Name, auth.Name),
            new Claim(ClaimTypes.Email, auth.Email),
            new Claim(ClaimTypes.Role, auth.Role),
        };
        var props = new AuthenticationProperties
        {
            ExpiresUtc = auth.ExpiresUtc,
            IsPersistent = false,
        };
        props.StoreTokens([new AuthenticationToken { Name = "access_token", Value = auth.Token }]);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
            ),
            props
        );
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        cart.Clear();
        await HttpContext.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
