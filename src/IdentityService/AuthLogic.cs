using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShopSphere.Shared;

namespace IdentityService;

public class AuthLogic(IdentityDb db, IPasswordHasher<UserAccount> hasher, IConfiguration config)
{
    public static bool StrongPassword(string p) =>
        p.Length >= 12
        && p.Length <= 128
        && p.Any(char.IsUpper)
        && p.Any(char.IsLower)
        && p.Any(char.IsDigit)
        && p.Any(c => !char.IsLetterOrDigit(c));

    public async Task<AuthDto> Register(RegisterInput input, CancellationToken ct)
    {
        if (!StrongPassword(input.Password))
            throw new DomainException(
                400,
                "Use 12–128 characters including upper and lowercase letters, a number, and a symbol."
            );
        var email = input.Email.Trim().ToUpperInvariant();
        var name = input.Name.Trim();
        if (name.Length < 2)
            throw new DomainException(400, "Name must contain at least two characters.");
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DomainException(409, "Email is already registered.");
        var user = new UserAccount { Email = email, Name = name };
        user.PasswordHash = hasher.HashPassword(user, input.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new DomainException(
                409,
                "Account could not be created. Email may already be registered."
            );
        }
        return Token(user);
    }

    public async Task<AuthDto> Login(LoginInput input, CancellationToken ct)
    {
        var email = input.Email.Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        // Hash verification also runs for unknown emails to reduce account-enumeration timing differences.
        var candidate =
            user
            ?? new UserAccount
            {
                PasswordHash = hasher.HashPassword(new UserAccount(), "Dummy-password-728!"),
            };
        var result = hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, input.Password);
        if (user == null || result == PasswordVerificationResult.Failed)
            throw new DomainException(401, "Email or password is incorrect.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            await db.SaveChangesAsync(ct);
        }
        return Token(user);
    }

    public AuthDto Token(UserAccount user)
    {
        var expires = DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:ExpirationMinutes", 60));
        var token = new JwtSecurityToken(
            config.Required("Jwt:Issuer"),
            config.Required("Jwt:Audience"),
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.Name),
                new Claim("email", user.Email),
                new Claim("role", user.Role),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Required("Jwt:Key"))),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            user.Id.ToString(),
            user.Name,
            user.Email,
            user.Role
        );
    }
}
