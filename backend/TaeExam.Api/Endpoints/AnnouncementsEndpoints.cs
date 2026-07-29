using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AnnouncementsEndpoints
{
    public static void MapAnnouncementsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/announcements").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, HttpContext http, bool includeInactive = false) =>
        {
            var now = DateTime.UtcNow;
            var query = db.Announcements.Include(a => a.CreatedByUser).AsQueryable();
            if (!(includeInactive && CurrentUser.IsAdmin(http.User)))
            {
                query = query.Where(a => a.PublishAtUtc <= now && (a.ExpireAtUtc == null || a.ExpireAtUtc >= now));
            }

            var items = await query
                .OrderByDescending(a => a.Priority).ThenByDescending(a => a.PublishAtUtc)
                .Select(a => new AnnouncementDto(a.Id, a.Title, a.Content, a.PublishAtUtc, a.ExpireAtUtc, a.Priority, a.CreatedByUser!.Username, a.CreatedAtUtc))
                .ToListAsync();
            return Results.Ok(items);
        }).RequirePermission("announcement:view");

        group.MapPost("/", async (CreateAnnouncementRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var userId = CurrentUser.Id(http.User);
            var announcement = new Announcement
            {
                Title = req.Title,
                Content = req.Content,
                PublishAtUtc = req.PublishAtUtc,
                ExpireAtUtc = req.ExpireAtUtc,
                Priority = req.Priority,
                CreatedByUserId = userId,
            };
            db.Announcements.Add(announcement);
            await db.SaveChangesAsync();
            audit.Log(http, "PublishAnnouncement", "Announcement", announcement.Id, req.Title);
            return Results.Ok(new { announcement.Id });
        }).RequirePermission("announcement:manage");

        group.MapPut("/{id:int}", async (int id, CreateAnnouncementRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
            if (announcement is null) return Results.NotFound();

            announcement.Title = req.Title;
            announcement.Content = req.Content;
            announcement.PublishAtUtc = req.PublishAtUtc;
            announcement.ExpireAtUtc = req.ExpireAtUtc;
            announcement.Priority = req.Priority;
            await db.SaveChangesAsync();
            audit.Log(http, "UpdateAnnouncement", "Announcement", id, req.Title);
            return Results.Ok();
        }).RequirePermission("announcement:manage");
    }
}
