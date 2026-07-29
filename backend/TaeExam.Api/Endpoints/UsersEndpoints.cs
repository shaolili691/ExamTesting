using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class UsersEndpoints
{
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

        group.MapPut("/me", async (UpdateProfileRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var userId = CurrentUser.Id(http.User);
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.Email = req.Email;
            await db.SaveChangesAsync();
            audit.Log(http, "UpdateProfile", "User", userId);
            return Results.Ok();
        }).RequireAuthorization();

        group.MapGet("/roles", async (AppDbContext db) =>
        {
            var roles = await db.Roles.OrderBy(r => r.Id).Select(r => new { r.Id, r.Name }).ToListAsync();
            return Results.Ok(roles);
        }).RequirePermission("user:view");
    }
}

public record UpdateProfileRequest(string Email);
