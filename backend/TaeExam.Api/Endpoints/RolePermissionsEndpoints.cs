using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class RolePermissionsEndpoints
{
    // Administrator must always keep these two — otherwise an admin could lock every
    // Administrator account out of user management and out of this settings screen itself,
    // with no way back in short of a database edit.
    private static readonly HashSet<string> ProtectedAdministratorKeys = ["permission:manage", "user:manage"];
    private static readonly string[] ValidRoles = [RoleNames.Administrator, RoleNames.Teacher, RoleNames.Student];

    public static void MapRolePermissionsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/role-permissions");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var assigned = await db.RolePermissions
                .GroupBy(rp => rp.PermissionKey)
                .ToDictionaryAsync(g => g.Key, g => g.Select(rp => rp.RoleName).ToList());

            var rows = Permissions.Catalog
                .Select(c => new RolePermissionRowDto(c.Key, c.Label, assigned.GetValueOrDefault(c.Key, [])))
                .ToList();
            return Results.Ok(rows);
        }).RequirePermission("permission:manage");

        group.MapGet("/me", async (AppDbContext db, HttpContext http) =>
        {
            var role = http.User.FindFirst(ClaimTypes.Role)?.Value;
            if (role is null) return Results.Ok(Array.Empty<string>());
            var keys = await db.RolePermissions.Where(rp => rp.RoleName == role).Select(rp => rp.PermissionKey).ToListAsync();
            return Results.Ok(keys);
        }).RequireAuthorization();

        group.MapPut("/{permissionKey}/{roleName}", async (string permissionKey, string roleName, UpdateRolePermissionRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            if (!Permissions.Catalog.Any(c => c.Key == permissionKey))
            {
                return Results.BadRequest(new { error = $"Unknown permission key '{permissionKey}'" });
            }
            if (!ValidRoles.Contains(roleName))
            {
                return Results.BadRequest(new { error = $"Unknown role '{roleName}'" });
            }
            if (!req.Allowed && roleName == RoleNames.Administrator && ProtectedAdministratorKeys.Contains(permissionKey))
            {
                return Results.BadRequest(new { error = "Cannot revoke Administrator access to this permission — it would lock every admin out." });
            }

            var existing = await db.RolePermissions.FirstOrDefaultAsync(rp => rp.PermissionKey == permissionKey && rp.RoleName == roleName);
            if (req.Allowed && existing is null)
            {
                db.RolePermissions.Add(new RolePermission { PermissionKey = permissionKey, RoleName = roleName });
            }
            else if (!req.Allowed && existing is not null)
            {
                db.RolePermissions.Remove(existing);
            }
            await db.SaveChangesAsync();
            audit.Log(http, "UpdateRolePermission", "RolePermission", null, $"{permissionKey} / {roleName} = {req.Allowed}");
            return Results.Ok();
        }).RequirePermission("permission:manage");
    }
}
