using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AttemptsEndpoints
{
    public static void MapAttemptsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/attempts").RequireAuthorization();

        group.MapPost("/", async (StartAttemptRequest req, AppDbContext db, HttpContext http, IAuditLogService audit) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == req.ExamId);
            if (exam is null || !(exam.UserId == null || exam.UserId == callerId || isAdmin))
            {
                return Results.NotFound(new { error = "Exam not found" });
            }

            var existing = await db.Attempts.FirstOrDefaultAsync(a =>
                a.ExamId == req.ExamId && a.UserId == callerId && a.Status == AttemptStatus.InProgress);
            if (existing is not null)
            {
                return Results.Ok(new StartAttemptResponse(existing.Id, Resumed: true));
            }

            var attempt = new Attempt { ExamId = req.ExamId, UserId = callerId };
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            audit.Log(http, "StartExam", "Attempt", attempt.Id);
            return Results.Ok(new StartAttemptResponse(attempt.Id, Resumed: false));
        }).RequirePermission("exam:start");

        group.MapPost("/{id:int}/answer", async (int id, AnswerSubmission req, AppDbContext db, HttpContext http) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == id);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != callerId && !isAdmin) return Results.NotFound();
            if (attempt.Status == AttemptStatus.Submitted) return Results.BadRequest(new { error = "Attempt already submitted" });

            var eq = await db.ExamQuestions.Include(x => x.Question)
                .FirstOrDefaultAsync(x => x.Id == req.ExamQuestionId && x.ExamId == attempt.ExamId);
            if (eq is null) return Results.BadRequest(new { error = "Question does not belong to this exam" });

            var correct = ScoringService.IsCorrect(req.SelectedIndexes, eq.Question!.CorrectIndexes);
            var pointsAwarded = correct ? eq.PointsOverride : 0;

            var existingAnswer = await db.AttemptAnswers.FirstOrDefaultAsync(a => a.AttemptId == id && a.ExamQuestionId == req.ExamQuestionId);
            if (existingAnswer is null)
            {
                db.AttemptAnswers.Add(new AttemptAnswer
                {
                    AttemptId = id,
                    ExamQuestionId = req.ExamQuestionId,
                    SelectedIndexes = req.SelectedIndexes,
                    IsCorrect = correct,
                    PointsAwarded = pointsAwarded,
                    Marked = req.Marked,
                });
            }
            else
            {
                existingAnswer.SelectedIndexes = req.SelectedIndexes;
                existingAnswer.IsCorrect = correct;
                existingAnswer.PointsAwarded = pointsAwarded;
                existingAnswer.Marked = req.Marked;
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequirePermission("exam:submit");

        group.MapGet("/{id:int}/state", async (int id, AppDbContext db, HttpContext http) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var attempt = await db.Attempts
                .Include(a => a.Exam)
                .Include(a => a.Answers)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != callerId && !isAdmin) return Results.NotFound();

            var answers = attempt.Answers
                .Select(a => new AnswerStateDto(a.ExamQuestionId, a.SelectedIndexes, a.Marked))
                .ToList();
            return Results.Ok(new AttemptStateDto(attempt.Id, attempt.ExamId, attempt.Status == AttemptStatus.InProgress, attempt.StartedAtUtc, attempt.Exam!.TimeLimitMinutes, answers));
        }).RequirePermission("exam:start");

        group.MapPost("/{id:int}/submit", async (int id, AppDbContext db, HttpContext http, IAuditLogService audit) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var attempt = await db.Attempts
                .Include(a => a.Exam!).ThenInclude(e => e.ExamQuestions).ThenInclude(eq => eq.Question)
                .Include(a => a.Answers)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (attempt is null) return Results.NotFound(new { error = "Attempt not found" });
            if (attempt.UserId != callerId && !isAdmin) return Results.NotFound(new { error = "Attempt not found" });
            if (attempt.Status == AttemptStatus.Submitted) return Results.BadRequest(new { error = "Attempt already submitted" });

            var answeredExamQuestionIds = attempt.Answers.Select(a => a.ExamQuestionId).ToHashSet();

            var maxScore = 0;
            var score = 0;
            foreach (var eq in attempt.Exam!.ExamQuestions)
            {
                if (!answeredExamQuestionIds.Contains(eq.Id))
                {
                    // Never answered (including autosave) — backfill as an empty, incorrect answer.
                    db.AttemptAnswers.Add(new AttemptAnswer
                    {
                        AttemptId = attempt.Id,
                        ExamQuestionId = eq.Id,
                        SelectedIndexes = new List<int>(),
                        IsCorrect = false,
                        PointsAwarded = 0,
                    });
                }
                maxScore += eq.PointsOverride;
            }
            // Sum score from whatever is already persisted via autosave (IsCorrect/PointsAwarded were
            // computed at answer time) — never re-trust anything from the client at submit time.
            score = attempt.Answers.Sum(a => a.PointsAwarded);

            attempt.Score = score;
            attempt.MaxScore = maxScore;
            attempt.PercentScore = maxScore > 0 ? Math.Round(100m * score / maxScore, 1) : 0;
            attempt.Passed = score >= attempt.Exam.PassThresholdPoints;
            attempt.Status = AttemptStatus.Submitted;
            attempt.SubmittedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            var elapsedSeconds = attempt.SubmittedAtUtc.HasValue
                ? (int)(attempt.SubmittedAtUtc.Value - attempt.StartedAtUtc).TotalSeconds
                : (int?)null;
            audit.Log(http, "SubmitExam", "Attempt", attempt.Id,
                $"'{attempt.Exam.Title}', score {score}/{maxScore}, took {elapsedSeconds}s");

            var result = await ScoringService.BuildAttemptResultAsync(db, attempt.Id);
            return Results.Ok(result);
        }).RequirePermission("exam:submit");

        group.MapGet("/", async (AppDbContext db, HttpContext http) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            // Only submitted attempts count as "history" — an in-progress/abandoned attempt isn't a
            // result yet; it's resumed transparently via POST / when the user reopens that exam.
            var query = db.Attempts.Include(a => a.Exam).Where(a => a.Status == AttemptStatus.Submitted).AsQueryable();
            if (!isAdmin) query = query.Where(a => a.UserId == callerId);

            var attempts = await query.OrderByDescending(a => a.StartedAtUtc).ToListAsync();

            // Only admins see a mix of everyone's attempts, so only admins need the "who" column —
            // a student's own history is implicitly all theirs.
            var usernames = isAdmin
                ? await db.Users.Where(u => attempts.Select(a => a.UserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username)
                : new Dictionary<int, string>();

            var dtos = attempts
                .Select(a => new AttemptSummaryDto(a.Id, a.ExamId, a.Exam!.Title, a.Exam.Type.ToString(), a.Score, a.MaxScore, a.PercentScore, a.Passed, a.SubmittedAtUtc, a.Status.ToString(),
                    isAdmin ? usernames.GetValueOrDefault(a.UserId) : null))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, HttpContext http, IAuditLogService audit) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == id);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != callerId && !isAdmin) return Results.NotFound();

            var result = await ScoringService.BuildAttemptResultAsync(db, id);
            audit.Log(http, "ReviewExam", "Attempt", id);
            return Results.Ok(result);
        }).RequirePermission("exam:review");
    }
}
