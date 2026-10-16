using System.Net;
using System.Net.Http.Json;
using IdentityService;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopSphere.Shared;

namespace ShopSphere.Tests;

public class IdentityTests
{
    [Fact]
    public async Task Registration_hashes_password_and_issues_customer_token()
    {
        using var f = new IdentityFactory();
        using var c = f.Start();
        var input = new RegisterInput
        {
            Name = "Test Customer",
            Email = "customer@example.test",
            Password = "Strong-test-password-123!",
        };
        var response = await c.PostAsJsonAsync("/api/auth/register", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDto>())!;
        Assert.Equal("Customer", auth.Role);
        using var scope = f.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IdentityDb>().Users.SingleAsync();
        Assert.NotEqual(input.Password, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<UserAccount>().VerifyHashedPassword(
                user,
                user.PasswordHash,
                input.Password
            )
        );
        c.As(auth.Token);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.GetAsync("/api/auth/admin-check")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await c.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginInput
                    {
                        Email = input.Email.ToUpperInvariant(),
                        Password = input.Password,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync("/api/auth/register", input)).StatusCode
        );
    }

    [Theory]
    [InlineData("short")]
    [InlineData("longbutnocomplexity")]
    public async Task Weak_password_is_rejected(string password)
    {
        using var f = new IdentityFactory();
        using var c = f.Start();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await c.PostAsJsonAsync(
                    "/api/auth/register",
                    new RegisterInput
                    {
                        Name = "Test User",
                        Email = "test@example.test",
                        Password = password,
                    }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Incorrect_login_is_unauthorized()
    {
        using var f = new IdentityFactory();
        using var c = f.Start();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await c.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginInput
                    {
                        Email = "unknown@example.test",
                        Password = "Incorrect-password-123!",
                    }
                )
            ).StatusCode
        );
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("audience")]
    public async Task Invalid_tokens_are_rejected(string kind)
    {
        using var f = new IdentityFactory();
        using var c = f.Start();
        var token = TestConfig.Token(
            expired: kind == "expired",
            key: kind == "signature"
                ? "A-different-long-key-to-test-invalid-signatures!"
                : TestConfig.Key,
            audience: kind == "audience" ? "other" : "ShopSphere"
        );
        c.As(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Valid_admin_token_accesses_admin_endpoint()
    {
        using var f = new IdentityFactory();
        using var c = f.Start().As(TestConfig.Token("Administrator"));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/admin-check")).StatusCode);
    }
}
