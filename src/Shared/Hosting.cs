using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ShopSphere.Shared;

public sealed class DomainException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception ex,
        CancellationToken ct
    )
    {
        var status =
            ex is DomainException d ? d.Status
            : ex is HttpRequestException or TaskCanceledException ? 503
            : 500;
        if (status >= 500)
            logger.LogError(ex, "Request failed {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = status >= 500 ? "A service is temporarily unavailable." : ex.Message,
                Extensions = { ["traceId"] = context.TraceIdentifier },
            },
            ct
        );
        return true;
    }
}

public static class Hosting
{
    public static string Required(this IConfiguration c, string name) =>
        c[name] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Configuration '{name}' is required. See docs/LOCAL_SETUP.md."
            );

    public static void AddServiceDefaults(this WebApplicationBuilder b)
    {
        b.Services.AddControllers();
        b.Services.AddProblemDetails();
        b.Services.AddExceptionHandler<ApiExceptionHandler>();
        var key = b.Configuration.Required("Jwt:Key");
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes.");
        b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = b.Configuration.Required("Jwt:Issuer"),
                    ValidAudience = b.Configuration.Required("Jwt:Audience"),
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(15),
                };
            });
        b.Services.AddAuthorization();
    }

    public static void UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async ctx =>
        {
            await ctx.HttpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = ctx.HttpContext.Response.StatusCode,
                    Title = Reason(ctx.HttpContext.Response.StatusCode),
                }
            );
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
    }

    static string Reason(int code) =>
        code switch
        {
            401 => "Authentication required.",
            403 => "Access denied.",
            404 => "Resource not found.",
            _ => "Request could not be completed.",
        };

    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new DomainException(401, "Authentication required.");

    public static void RequireService(HttpContext context, IConfiguration config)
    {
        var expected = Encoding.UTF8.GetBytes(config.Required("Services:Key"));
        var actual = Encoding.UTF8.GetBytes(context.Request.Headers["X-Service-Key"].ToString());
        if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new DomainException(403, "Invalid service credentials.");
    }

    public static void ConfigureServiceClient(
        this HttpClient client,
        IConfiguration config,
        string name
    )
    {
        client.BaseAddress = new Uri(config.Required($"Services:{name}"));
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("X-Service-Key", config.Required("Services:Key"));
    }

    public static async Task EnsureApiSuccess(
        this HttpResponseMessage response,
        CancellationToken ct = default
    )
    {
        if (response.IsSuccessStatusCode)
            return;
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        throw new DomainException(
            (int)response.StatusCode,
            problem?.Title ?? "Remote service rejected the request."
        );
    }
}
