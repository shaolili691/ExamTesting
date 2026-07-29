using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Endpoints;

public static class WrongQuestionsEndpoints
{
    public static void MapWrongQuestionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wrong-questions", async (AppDbContext db, HttpContext http, int? userId = null) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var targetUserId = (userId.HasValue && CurrentUser.IsAdmin(http.User)) ? userId.Value : callerId;

            var wrong = await db.AttemptAnswers
                .Include(a => a.Attempt)
                .Include(a => a.ExamQuestion!).ThenInclude(eq => eq.Question)
                .Where(a => a.Attempt!.Status == AttemptStatus.Submitted && a.Attempt.UserId == targetUserId && !a.IsCorrect)
                .OrderByDescending(a => a.Attempt!.SubmittedAtUtc)
                .ToListAsync();

            var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);

            var deduped = wrong
                .GroupBy(a => a.ExamQuestion!.QuestionId)
                .Select(g =>
                {
                    var first = g.First(); // most-recently-missed, due to the ordering above
                    var q = first.ExamQuestion!.Question!;
                    return new WrongQuestionDto(
                        q.Id,
                        q.Chapter,
                        chapterTitles.GetValueOrDefault(q.Chapter),
                        q.Topic,
                        q.Level,
                        q.IsScenario,
                        q.ScenarioText,
                        q.QuestionText,
                        q.Options,
                        q.CorrectIndexes,
                        first.SelectedIndexes,
                        q.Explanation,
                        first.Attempt!.SubmittedAtUtc!.Value,
                        g.Count());
                })
                .OrderByDescending(d => d.LastMissedAtUtc)
                .ToList();

            return Results.Ok(deduped);
        }).RequirePermission("wrongquestion:view");
    }
}
