using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShopSphere.Shared;

namespace IdentityService;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public class AuthController(AuthLogic auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthDto>> Register(RegisterInput input, CancellationToken ct) =>
        StatusCode(201, await auth.Register(input, ct));

    [HttpPost("login")]
    public Task<AuthDto> Login(LoginInput input, CancellationToken ct) => auth.Login(input, ct);

    [HttpGet("me"), Authorize]
    public object Me() =>
        new
        {
            Id = User.UserId(),
            Name = User.Identity?.Name,
            Email = User.FindFirst("email")?.Value,
            Role = User.FindFirst("role")?.Value,
        };

    [HttpGet("admin-check"), Authorize(Roles = "Administrator")]
    public object AdminCheck() => new { Authorized = true };
}
