using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class AuthResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public AuthResponse? Response { get; init; }

    public static AuthResult Fail(string error) => new() { Success = false, Error = error };
    public static AuthResult Ok(AuthResponse response) => new() { Success = true, Response = response };
}

public class AuthService(AppDbContext db, IConfiguration config)
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public async Task<AuthResult> RegisterAsync(string username, string email, string password)
    {
        if (await db.Users.AnyAsync(u => u.Username == username))
        {
            return AuthResult.Fail("Username already taken");
        }

        var studentRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.Student);
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11),
            RoleId = studentRole.Id,
            Status = UserStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return AuthResult.Ok(await IssueTokensAsync(user, studentRole.Name));
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            return AuthResult.Fail("Invalid username or password");
        }

        if (user.Status == UserStatus.Disabled)
        {
            return AuthResult.Fail("This account has been disabled");
        }

        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > DateTime.UtcNow)
        {
            return AuthResult.Fail("Account is temporarily locked due to too many failed login attempts");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            }
            await db.SaveChangesAsync();
            return AuthResult.Fail("Invalid username or password");
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntilUtc = null;
        await db.SaveChangesAsync();

        return AuthResult.Ok(await IssueTokensAsync(user, user.Role!.Name));
    }

    public async Task<AuthResult> RefreshAsync(string rawToken)
    {
        var tokenHash = Hash(rawToken);
        var existing = await db.RefreshTokens.Include(r => r.User).ThenInclude(u => u!.Role)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (existing is null || !existing.IsActive || existing.User is null)
        {
            return AuthResult.Fail("Invalid or expired refresh token");
        }

        existing.RevokedAtUtc = DateTime.UtcNow;
        var response = await IssueTokensAsync(existing.User, existing.User.Role!.Name);
        return AuthResult.Ok(response);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string oldPassword, string newPassword, string confirmPassword)
    {
        if (newPassword != confirmPassword)
        {
            return (false, "New password and confirmation do not match");
        }
        if (newPassword.Length < 6)
        {
            return (false, "New password must be at least 6 characters");
        }

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
        {
            return (false, "Current password is incorrect");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 11);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task LogoutAsync(string rawToken)
    {
        var tokenHash = Hash(rawToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash);
        if (existing is not null && existing.RevokedAtUtc is null)
        {
            existing.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string roleName)
    {
        var jwt = config.GetSection("Jwt");
        var accessMinutes = jwt.GetValue<int>("AccessTokenMinutes", 120);
        var refreshDays = jwt.GetValue<int>("RefreshTokenDays", 14);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(accessMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("username", user.Username),
            new Claim(ClaimTypes.Role, roleName),
            new Claim(ClaimTypes.Email, user.Email),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: creds);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawRefreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshDays),
        });
        await db.SaveChangesAsync();

        return new AuthResponse(
            accessToken,
            rawRefreshToken,
            expiresAtUtc,
            new UserSummaryDto(user.Id, user.Username, user.Email, roleName));
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
