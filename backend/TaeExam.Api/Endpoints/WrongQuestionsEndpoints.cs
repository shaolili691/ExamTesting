using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class WrongQuestionsEndpoints
{
    public static void MapWrongQuestionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wrong-questions", async (AppDbContext db, WrongQuestionService wrongQuestions, HttpContext http, int? userId = null) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var targetUserId = (userId.HasValue && CurrentUser.IsAdmin(http.User)) ? userId.Value : callerId;

            var open = await wrongQuestions.GetOpenWrongQuestionsAsync(targetUserId);
            var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);

            var dtos = open
                .Select(o =>
                {
                    var q = o.Question;
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
                        o.LastSelectedIndexes,
                        q.Explanation,
                        q.Points,
                        o.LastMissedAtUtc,
                        o.TimesMissed);
                })
                .OrderByDescending(d => d.LastMissedAtUtc)
                .ToList();

            return Results.Ok(dtos);
        }).RequirePermission("wrongquestion:view");
    }
}
