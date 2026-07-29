using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, AuthService auth) =>
        {
            var result = await auth.RegisterAsync(req.Username, req.Email, req.Password);
            if (!result.Success) return Results.BadRequest(new { error = result.Error });
            return Results.Ok(result.Response);
        });

        group.MapPost("/login", async (LoginRequest req, AuthService auth, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var result = await auth.LoginAsync(req.Username, req.Password);
            if (!result.Success)
            {
                var existingUserId = await db.Users.Where(u => u.Username == req.Username).Select(u => (int?)u.Id).FirstOrDefaultAsync();
                audit.Log(http, "Login", "User", existingUserId, $"Failed login for '{req.Username}': {result.Error}", userIdOverride: existingUserId);
                return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
            }
            audit.Log(http, "Login", "User", result.Response!.User.Id, "Login succeeded", userIdOverride: result.Response.User.Id);
            return Results.Ok(result.Response);
        });

        group.MapPost("/refresh", async (RefreshRequest req, AuthService auth) =>
        {
            var result = await auth.RefreshAsync(req.RefreshToken);
            if (!result.Success) return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
            return Results.Ok(result.Response);
        });

        group.MapPost("/logout", async (LogoutRequest req, AuthService auth, IAuditLogService audit, HttpContext http) =>
        {
            await auth.LogoutAsync(req.RefreshToken);
            audit.Log(http, "Logout", "User");
            return Results.Ok();
        }).RequireAuthorization();

        group.MapGet("/me", async (AppDbContext db, HttpContext http) =>
        {
            var userId = CurrentUser.Id(http.User);
            var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.NotFound();
            return Results.Ok(new UserSummaryDto(user.Id, user.Username, user.Email, user.Role!.Name));
        }).RequireAuthorization();
    }
}
