using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;

namespace TaeExam.Api.Endpoints;

public static class ExamCategoriesEndpoints
{
    public static void MapExamCategoriesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/exam-categories", async (AppDbContext db) =>
        {
            var categories = await db.ExamCategories
                .OrderBy(c => c.Id)
                .Select(c => new ExamCategoryDto(c.Id, c.Name))
                .ToListAsync();
            return Results.Ok(categories);
        }).RequireAuthorization();
    }
}
