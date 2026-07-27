using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;

namespace TaeExam.Api.Endpoints;

public static class SyllabusEndpoints
{
    public static void MapSyllabusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/syllabus-chapters", async (AppDbContext db) =>
        {
            var chapters = await db.SyllabusChapters.OrderBy(c => c.Number).ToListAsync();
            return Results.Ok(chapters);
        });
    }
}
