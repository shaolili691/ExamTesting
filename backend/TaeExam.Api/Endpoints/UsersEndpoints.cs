using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class UsersEndpoints
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled);
    private static readonly string[] AllowedGenders = ["Male", "Female", "Other"];

    public static void MapUsersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var users = await db.Users.Include(u => u.Role)
                .OrderBy(u => u.Username)
                .Select(u => new AdminUserDto(u.Id, u.Username, u.Email, u.Role!.Name, u.Status == UserStatus.Active, u.CreatedAtUtc))
                .ToListAsync();
            return Results.Ok(users);
        }).RequirePermission("user:view");

        group.MapPost("/", async (CreateUserRequest req, AppDbContext db) =>
        {
            if (await db.Users.AnyAsync(u => u.Username == req.Username))
            {
                return Results.BadRequest(new { error = "Username already taken" });
            }
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == req.RoleName);
            if (role is null) return Results.BadRequest(new { error = $"Unknown role '{req.RoleName}'" });

            var user = new User
            {
                Username = req.Username,
                Email = req.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11),
                RoleId = role.Id,
                Status = UserStatus.Active,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Ok(new AdminUserDto(user.Id, user.Username, user.Email, role.Name, true, user.CreatedAtUtc));
        }).RequirePermission("user:manage");

        group.MapPut("/{id:int}", async (int id, UpdateUserRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == req.RoleName);
            if (role is null) return Results.BadRequest(new { error = $"Unknown role '{req.RoleName}'" });

            user.Email = req.Email;
            user.RoleId = role.Id;
            user.Status = req.Active ? UserStatus.Active : UserStatus.Disabled;
            await db.SaveChangesAsync();
            audit.Log(http, "UpdateUser", "User", id, $"Set role={req.RoleName}, active={req.Active}");
            return Results.Ok(new AdminUserDto(user.Id, user.Username, user.Email, role.Name, req.Active, user.CreatedAtUtc));
        }).RequirePermission("user:manage");

        group.MapGet("/me", async (AppDbContext db, HttpContext http) =>
        {
            var userId = CurrentUser.Id(http.User);
            var user = await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);
            return Results.Ok(new UserProfileDto(user.Id, user.Username, user.Email, user.Role!.Name,
                user.RealName, user.Age, user.Gender, user.BirthDate, user.Phone, user.AvatarUrl, user.CreatedAtUtc));
        }).RequireAuthorization();

        group.MapPut("/me", async (UpdateProfileRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var email = req.Email.Trim();
            if (!EmailPattern.IsMatch(email)) return Results.BadRequest(new { error = "Invalid email format" });

            string? phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
            if (phone is not null && !PhonePattern.IsMatch(phone)) return Results.BadRequest(new { error = "Invalid phone format" });

            string? gender = string.IsNullOrWhiteSpace(req.Gender) ? null : req.Gender.Trim();
            if (gender is not null && !AllowedGenders.Contains(gender)) return Results.BadRequest(new { error = $"Gender must be one of: {string.Join(", ", AllowedGenders)}" });

            if (req.Age is < 0 or > 150) return Results.BadRequest(new { error = "Age must be between 0 and 150" });

            if (req.BirthDate is { } bd && bd > DateOnly.FromDateTime(DateTime.UtcNow)) return Results.BadRequest(new { error = "Birth date cannot be in the future" });

            string? realName = string.IsNullOrWhiteSpace(req.RealName) ? null : req.RealName.Trim();
            string? avatarUrl = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim();
            if (avatarUrl is { Length: > 2_000_000 }) return Results.BadRequest(new { error = "Avatar image is too large" });

            var userId = CurrentUser.Id(http.User);
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.Email = email;
            user.RealName = realName;
            user.Age = req.Age;
            user.Gender = gender;
            user.BirthDate = req.BirthDate;
            user.Phone = phone;
            user.AvatarUrl = avatarUrl;
            await db.SaveChangesAsync();
            audit.Log(http, "UpdateProfile", "User", userId, "Updated personal details");
            return Results.Ok();
        }).RequireAuthorization();

        // 用户动态: the current user's own activity timeline, built from the shared AuditLog
        // table (same source as the admin-only /api/audit-logs feed) but scoped to the caller
        // and rendered with a human sentence per row instead of raw Action/Entity/Description.
        group.MapGet("/me/activities", async (AppDbContext db, HttpContext http, int page = 1, int pageSize = 20) =>
        {
            var userId = CurrentUser.Id(http.User);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.AuditLogs.Include(a => a.User).Where(a => a.UserId == userId);
            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(a => a.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = rows.Select(a => new UserActivityDto(a.CreatedAtUtc, a.Action, DescribeActivity(a))).ToList();
            return Results.Ok(new { total, page, pageSize, items });
        }).RequireAuthorization();

        group.MapGet("/roles", async (AppDbContext db) =>
        {
            var roles = await db.Roles.OrderBy(r => r.Id).Select(r => new { r.Id, r.Name }).ToListAsync();
            return Results.Ok(roles);
        }).RequirePermission("user:view");
    }

    // Turns a raw AuditLog row into the one-line sentence the 用户动态 timeline shows.
    // Falls back to "{username} {action}" for any Action not covered here (e.g. new event
    // types that reuse the AuditLog pipeline before this switch is updated for them).
    private static string DescribeActivity(AuditLog a)
    {
        var name = a.User?.Username ?? "User";
        return a.Action switch
        {
            "Login" => $"{name} logged in",
            "Logout" => $"{name} logged out",
            "SubmitExam" => $"{name} submitted an exam" + (a.Description is { } d ? $" — {d}" : ""),
            "CreatePaper" => $"{name} generated a practice paper" + (a.Description is { } d ? $" — {d}" : ""),
            "StartExam" => $"{name} started an exam",
            "UpdateProfile" => $"{name} updated their profile",
            "ChangePassword" => $"{name} changed their password",
            _ => $"{name} {a.Action}" + (a.Description is { } d ? $" — {d}" : ""),
        };
    }
}

public record UpdateProfileRequest(
    string Email,
    string? RealName = null,
    int? Age = null,
    string? Gender = null,
    DateOnly? BirthDate = null,
    string? Phone = null,
    string? AvatarUrl = null);
