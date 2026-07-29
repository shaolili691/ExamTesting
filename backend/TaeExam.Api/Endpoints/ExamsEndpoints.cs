using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class ExamsEndpoints
{
    public static void MapExamsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/exams").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, HttpContext http, int? categoryId = null) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var query = db.Exams
                .Include(e => e.ExamQuestions)
                .Where(e => e.UserId == null || e.UserId == callerId || isAdmin);
            if (categoryId.HasValue) query = query.Where(e => e.CategoryId == categoryId);

            var exams = await query.OrderByDescending(e => e.CreatedAtUtc).ToListAsync();
            var categoryNames = await db.ExamCategories.ToDictionaryAsync(c => c.Id, c => c.Name);

            var dtos = exams
                .Select(e => new ExamSummaryDto(e.Id, e.Title, e.Type.ToString(), e.TotalPoints, e.PassThresholdPoints, e.ExamQuestions.Count, e.CreatedAtUtc,
                    e.CategoryId, e.CategoryId.HasValue ? categoryNames.GetValueOrDefault(e.CategoryId.Value) : null))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, HttpContext http) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var exam = await db.Exams
                .Include(e => e.ExamQuestions.OrderBy(eq => eq.OrderIndex))
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (exam is null || !(exam.UserId == null || exam.UserId == callerId || isAdmin)) return Results.NotFound();

            var questions = exam.ExamQuestions
                .Select(eq => new ExamQuestionDto(
                    eq.Id,
                    eq.OrderIndex,
                    eq.Question!.Chapter,
                    eq.Question.Level,
                    eq.Question.Topic,
                    eq.Question.IsScenario,
                    eq.Question.ScenarioText,
                    eq.Question.QuestionText,
                    eq.Question.Options,
                    eq.Question.IsMultiChoice,
                    eq.PointsOverride))
                .ToList();

            var dto = new ExamPaperDto(exam.Id, exam.Title, exam.Type.ToString(), exam.TotalPoints, exam.PassThresholdPoints, exam.TimeLimitMinutes, questions);
            return Results.Ok(dto);
        });

        group.MapPost("/generate", async (GenerateExamRequest? req, PaperGenerationService svc, HttpContext http, IAuditLogService audit) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var (exam, blueprint, warnings) = await svc.GenerateAsync(req ?? new GenerateExamRequest(), callerId);
            audit.Log(http, "CreatePaper", "Exam", exam.Id, "Generated paper");
            return Results.Ok(new GenerateExamResponse(exam.Id, blueprint, warnings));
        }).RequirePermission("paper:create");

        group.MapPost("/drill/{attemptId:int}", async (int attemptId, DrillRequest? req, DrillGenerationService svc, HttpContext http, IAuditLogService audit) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);
            try
            {
                var (exam, weakAreaSummary, coreCount, fillCount, warnings) = await svc.GenerateAsync(attemptId, req ?? new DrillRequest(), callerId, isAdmin);
                audit.Log(http, "CreatePaper", "Exam", exam.Id, $"Generated drill from attempt {attemptId}");
                return Results.Ok(new DrillResponse(exam.Id, weakAreaSummary, coreCount, fillCount, warnings));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission("paper:create");
    }
}
