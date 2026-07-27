using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AttemptsEndpoints
{
    public static void MapAttemptsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/attempts");

        group.MapPost("/", async (StartAttemptRequest req, AppDbContext db) =>
        {
            var examExists = await db.Exams.AnyAsync(e => e.Id == req.ExamId);
            if (!examExists) return Results.NotFound(new { error = "Exam not found" });

            var attempt = new Attempt { ExamId = req.ExamId };
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            return Results.Ok(new StartAttemptResponse(attempt.Id));
        });

        group.MapPost("/{id:int}/submit", async (int id, SubmitAttemptRequest req, AppDbContext db) =>
        {
            var attempt = await db.Attempts
                .Include(a => a.Exam!).ThenInclude(e => e.ExamQuestions)
                .ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (attempt is null) return Results.NotFound(new { error = "Attempt not found" });
            if (attempt.Status == AttemptStatus.Submitted) return Results.BadRequest(new { error = "Attempt already submitted" });

            var answerByExamQuestionId = req.Answers.ToDictionary(a => a.ExamQuestionId, a => a.SelectedIndexes);

            var maxScore = 0;
            var score = 0;
            foreach (var eq in attempt.Exam!.ExamQuestions)
            {
                var selected = answerByExamQuestionId.GetValueOrDefault(eq.Id, new List<int>());
                var correct = ScoringService.IsCorrect(selected, eq.Question!.CorrectIndexes);
                var pointsAwarded = correct ? eq.PointsOverride : 0;

                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = attempt.Id,
                    ExamQuestionId = eq.Id,
                    SelectedIndexes = selected,
                    IsCorrect = correct,
                    PointsAwarded = pointsAwarded,
                });

                maxScore += eq.PointsOverride;
                score += pointsAwarded;
            }

            attempt.Score = score;
            attempt.MaxScore = maxScore;
            attempt.PercentScore = maxScore > 0 ? Math.Round(100m * score / maxScore, 1) : 0;
            attempt.Passed = score >= attempt.Exam.PassThresholdPoints;
            attempt.Status = AttemptStatus.Submitted;
            attempt.SubmittedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            var result = await ScoringService.BuildAttemptResultAsync(db, attempt.Id);
            return Results.Ok(result);
        });

        group.MapGet("/", async (AppDbContext db) =>
        {
            var attempts = await db.Attempts
                .Include(a => a.Exam)
                .OrderByDescending(a => a.StartedAtUtc)
                .ToListAsync();

            var dtos = attempts
                .Select(a => new AttemptSummaryDto(a.Id, a.ExamId, a.Exam!.Title, a.Exam.Type.ToString(), a.Score, a.MaxScore, a.PercentScore, a.Passed, a.SubmittedAtUtc, a.Status.ToString()))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var exists = await db.Attempts.AnyAsync(a => a.Id == id);
            if (!exists) return Results.NotFound();

            var result = await ScoringService.BuildAttemptResultAsync(db, id);
            return Results.Ok(result);
        });
    }
}
