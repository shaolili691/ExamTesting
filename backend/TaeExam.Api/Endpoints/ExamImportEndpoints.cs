using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class ExamImportEndpoints
{
    public static void MapExamImportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/exam-import").RequireAuthorization();

        group.MapPost("/preview", async (IFormFile questionsFile, IFormFile? answersFile, ExamImportParsingService svc) =>
        {
            if (questionsFile is null || questionsFile.Length == 0)
            {
                return Results.BadRequest(new { error = "questionsFile is required." });
            }

            await using var qStream = questionsFile.OpenReadStream();
            Stream? aStream = (answersFile is not null && answersFile.Length > 0) ? answersFile.OpenReadStream() : null;
            try
            {
                var result = await svc.ParseAsync(qStream, aStream);
                return Results.Ok(result);
            }
            catch (ExamImportParseException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            finally
            {
                if (aStream is not null) await aStream.DisposeAsync();
            }
        }).RequirePermission("exam:import").DisableAntiforgery();

        group.MapPost("/confirm", async (ConfirmExamImportRequest req, AppDbContext db, HttpContext http, IAuditLogService audit) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Results.BadRequest(new { error = "Title is required." });
            }
            if (req.Questions.Count == 0)
            {
                return Results.BadRequest(new { error = "At least one question is required." });
            }

            if (!await db.ExamCategories.AnyAsync(c => c.Id == req.CategoryId))
            {
                return Results.BadRequest(new { error = "Unknown category." });
            }

            var validChapters = (await db.SyllabusChapters.Select(c => c.Code).ToListAsync()).ToHashSet();
            var details = new List<string>();
            for (var i = 0; i < req.Questions.Count; i++)
            {
                var q = req.Questions[i];
                var label = $"Question {i + 1}";
                if (string.IsNullOrWhiteSpace(q.Chapter) || !validChapters.Contains(q.Chapter))
                {
                    details.Add($"{label}: chapter must be one of the known syllabus chapters.");
                }
                if (q.Options.Count < 2)
                {
                    details.Add($"{label}: needs at least 2 options.");
                }
                if (q.CorrectIndexes.Count == 0 || q.CorrectIndexes.Any(ci => ci < 0 || ci >= q.Options.Count))
                {
                    details.Add($"{label}: needs at least one valid correct answer selected.");
                }
                if (q.Points <= 0)
                {
                    details.Add($"{label}: points must be greater than 0.");
                }
                if (string.IsNullOrWhiteSpace(q.QuestionText))
                {
                    details.Add($"{label}: question text is required.");
                }
            }
            if (details.Count > 0)
            {
                return Results.BadRequest(new { error = "Some questions need fixing before this exam can be saved.", details });
            }

            var exam = new Exam
            {
                Title = req.Title,
                Type = ExamType.Manual,
                CategoryId = req.CategoryId,
                UserId = null,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var order = 0;
            var totalPoints = 0;
            foreach (var q in req.Questions)
            {
                var question = new Question
                {
                    LegacyId = 0,
                    SourceFile = "manual-import",
                    Chapter = q.Chapter,
                    Topic = q.Topic,
                    Level = q.Level,
                    IsMultiChoice = q.IsMultiChoice,
                    IsScenario = q.IsScenario,
                    ScenarioText = q.ScenarioText,
                    QuestionText = q.QuestionText,
                    Options = q.Options,
                    CorrectIndexes = q.CorrectIndexes,
                    DistractorDesign = q.DistractorDesign,
                    Explanation = q.Explanation,
                    Points = q.Points,
                };
                db.Questions.Add(question);
                await db.SaveChangesAsync();

                db.ExamQuestions.Add(new ExamQuestion
                {
                    ExamId = exam.Id,
                    QuestionId = question.Id,
                    OrderIndex = order++,
                    PointsOverride = q.Points,
                });
                totalPoints += q.Points;
            }

            exam.TotalPoints = totalPoints;
            exam.PassThresholdPoints = (int)Math.Ceiling(totalPoints * 0.65m);
            exam.TimeLimitMinutes = (int)Math.Round(req.Questions.Count * 2.25);
            await db.SaveChangesAsync();

            audit.Log(http, "ImportExam", "Exam", exam.Id, $"Imported {req.Questions.Count} questions via PDF import");

            return Results.Ok(new ConfirmExamImportResponse(exam.Id, req.Questions.Count, totalPoints));
        }).RequirePermission("exam:import");
    }
}
