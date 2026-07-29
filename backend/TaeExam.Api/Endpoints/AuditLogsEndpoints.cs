using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;

namespace TaeExam.Api.Endpoints;

public static class AuditLogsEndpoints
{
    public static void MapAuditLogsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit-logs").RequirePermission("audit:view");

        group.MapGet("/", async (AppDbContext db, int page = 1, int pageSize = 50, int? userId = null, string? action = null, DateTime? from = null, DateTime? to = null) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.AuditLogs.Include(a => a.User).AsQueryable();
            if (userId.HasValue) query = query.Where(a => a.UserId == userId);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
            if (from.HasValue) query = query.Where(a => a.CreatedAtUtc >= from);
            if (to.HasValue) query = query.Where(a => a.CreatedAtUtc <= to);

            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(a => a.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogDto(a.Id, a.UserId, a.User != null ? a.User.Username : null, a.Action, a.Entity, a.EntityId, a.Description, a.Ip, a.Browser, a.CreatedAtUtc))
                .ToListAsync();

            return Results.Ok(new { total, page, pageSize, items = rows });
        });
    }
}
