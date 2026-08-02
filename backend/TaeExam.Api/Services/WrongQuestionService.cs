using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public record OpenWrongQuestion(Question Question, List<int> LastSelectedIndexes, DateTime LastMissedAtUtc, int TimesMissed);

// Shared by the wrong-question-book listing and full-book drill generation, so both agree on
// which questions still count as "wrong": a question drops out once the user has answered it
// correctly 3+ times in submitted attempts since the last time they missed it.
public class WrongQuestionService(AppDbContext db)
{
    public const int MasteryThreshold = 3;

    public async Task<List<OpenWrongQuestion>> GetOpenWrongQuestionsAsync(int userId)
    {
        var answers = await db.AttemptAnswers
            .Include(a => a.Attempt)
            .Include(a => a.ExamQuestion!).ThenInclude(eq => eq.Question)
            .Where(a => a.Attempt!.Status == AttemptStatus.Submitted && a.Attempt.UserId == userId)
            .OrderByDescending(a => a.Attempt!.SubmittedAtUtc)
            .ToListAsync();

        var open = new List<OpenWrongQuestion>();
        foreach (var g in answers.GroupBy(a => a.ExamQuestion!.QuestionId))
        {
            var lastWrong = g.FirstOrDefault(a => !a.IsCorrect); // list is already ordered by SubmittedAtUtc desc
            if (lastWrong is null) continue; // never missed

            var correctSinceLastMiss = g.Count(a => a.IsCorrect && a.Attempt!.SubmittedAtUtc > lastWrong.Attempt!.SubmittedAtUtc);
            if (correctSinceLastMiss >= MasteryThreshold) continue; // mastered — drop from the wrong-question book

            open.Add(new OpenWrongQuestion(
                lastWrong.ExamQuestion!.Question!,
                lastWrong.SelectedIndexes,
                lastWrong.Attempt!.SubmittedAtUtc!.Value,
                g.Count(a => !a.IsCorrect)));
        }
        return open;
    }
}
