using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class ExamsEndpoints
{
    public static void MapExamsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/exams");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var exams = await db.Exams
                .Include(e => e.ExamQuestions)
                .OrderByDescending(e => e.CreatedAtUtc)
                .ToListAsync();

            var dtos = exams
                .Select(e => new ExamSummaryDto(e.Id, e.Title, e.Type.ToString(), e.TotalPoints, e.PassThresholdPoints, e.ExamQuestions.Count, e.CreatedAtUtc))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var exam = await db.Exams
                .Include(e => e.ExamQuestions.OrderBy(eq => eq.OrderIndex))
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (exam is null) return Results.NotFound();

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

        group.MapPost("/generate", async (GenerateExamRequest? req, PaperGenerationService svc) =>
        {
            var (exam, blueprint, warnings) = await svc.GenerateAsync(req ?? new GenerateExamRequest());
            return Results.Ok(new GenerateExamResponse(exam.Id, blueprint, warnings));
        });

        group.MapPost("/drill/{attemptId:int}", async (int attemptId, DrillRequest? req, DrillGenerationService svc) =>
        {
            try
            {
                var (exam, weakAreaSummary, coreCount, fillCount, warnings) = await svc.GenerateAsync(attemptId, req ?? new DrillRequest());
                return Results.Ok(new DrillResponse(exam.Id, weakAreaSummary, coreCount, fillCount, warnings));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
