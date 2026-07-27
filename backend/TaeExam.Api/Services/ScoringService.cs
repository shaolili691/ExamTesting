using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public static class ScoringService
{
    public static bool IsCorrect(IEnumerable<int> selected, IEnumerable<int> correct)
    {
        var selectedSet = new HashSet<int>(selected);
        var correctSet = new HashSet<int>(correct);
        return selectedSet.SetEquals(correctSet);
    }

    public static List<ChapterBreakdownDto> BuildChapterBreakdown(
        IEnumerable<(Question Question, AttemptAnswer Answer)> rows,
        Dictionary<string, string> chapterTitles)
    {
        return rows
            .GroupBy(r => r.Question.Chapter)
            .Select(g => new ChapterBreakdownDto(
                Chapter: g.Key,
                ChapterTitle: chapterTitles.GetValueOrDefault(g.Key, g.Key),
                Correct: g.Count(r => r.Answer.IsCorrect),
                Total: g.Count(),
                PointsEarned: g.Sum(r => r.Answer.PointsAwarded),
                PointsMax: g.Sum(r => r.Answer.ExamQuestion!.PointsOverride)))
            .OrderBy(c => c.Chapter)
            .ToList();
    }

    public static async Task<AttemptResultDto> BuildAttemptResultAsync(AppDbContext db, int attemptId)
    {
        var attempt = await db.Attempts
            .Include(a => a.Exam)
            .Include(a => a.Answers).ThenInclude(ans => ans.ExamQuestion!).ThenInclude(eq => eq.Question)
            .FirstAsync(a => a.Id == attemptId);

        var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);

        var questionDtos = attempt.Answers
            .OrderBy(a => a.ExamQuestion!.OrderIndex)
            .Select(a => new QuestionReviewDto(
                a.ExamQuestionId,
                a.ExamQuestion!.Question!.Chapter,
                a.ExamQuestion.Question.Topic,
                a.ExamQuestion.Question.Level,
                a.ExamQuestion.Question.IsScenario,
                a.ExamQuestion.Question.ScenarioText,
                a.ExamQuestion.Question.QuestionText,
                a.ExamQuestion.Question.Options,
                a.ExamQuestion.PointsOverride,
                a.SelectedIndexes,
                a.ExamQuestion.Question.CorrectIndexes,
                a.IsCorrect,
                a.PointsAwarded,
                a.ExamQuestion.Question.Explanation,
                a.ExamQuestion.Question.DistractorDesign))
            .ToList();

        var chapterBreakdown = BuildChapterBreakdown(
            attempt.Answers.Select(a => (a.ExamQuestion!.Question!, a)),
            chapterTitles);

        return new AttemptResultDto(
            attempt.Id,
            attempt.ExamId,
            attempt.Exam!.Title,
            attempt.Score,
            attempt.MaxScore,
            attempt.PercentScore,
            attempt.Passed,
            attempt.SubmittedAtUtc,
            questionDtos,
            chapterBreakdown);
    }
}
