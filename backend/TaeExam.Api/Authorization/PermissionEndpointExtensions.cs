using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;

namespace TaeExam.Api.Authorization;

public static class PermissionEndpointExtensions
{
    private static async Task<bool> IsAllowed(HttpContext http, string permission)
    {
        var role = http.User.FindFirst(ClaimTypes.Role)?.Value;
        if (role is null) return false;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        return await db.RolePermissions.AnyAsync(rp => rp.PermissionKey == permission && rp.RoleName == role);
    }

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder.RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            if (!await IsAllowed(ctx.HttpContext, permission))
            {
                return Results.Json(new { error = $"Missing permission: {permission}" }, statusCode: StatusCodes.Status403Forbidden);
            }
            return await next(ctx);
        });
    }

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission)
    {
        builder.RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            if (!await IsAllowed(ctx.HttpContext, permission))
            {
                return Results.Json(new { error = $"Missing permission: {permission}" }, statusCode: StatusCodes.Status403Forbidden);
            }
            return await next(ctx);
        });
        return builder;
    }
}
