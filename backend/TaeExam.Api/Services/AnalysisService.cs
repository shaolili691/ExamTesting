using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class AnalysisService(AppDbContext db)
{
    public async Task<AnalysisOverviewDto> GetOverviewAsync()
    {
        var submittedAttempts = await db.Attempts
            .Where(a => a.Status == AttemptStatus.Submitted)
            .OrderBy(a => a.SubmittedAtUtc)
            .ToListAsync();

        var overall = new OverallStatsDto(
            TotalAttempts: submittedAttempts.Count,
            AvgPercent: submittedAttempts.Count > 0 ? Math.Round(submittedAttempts.Average(a => a.PercentScore), 1) : 0,
            PassRate: submittedAttempts.Count > 0 ? Math.Round(100m * submittedAttempts.Count(a => a.Passed) / submittedAttempts.Count, 1) : 0);

        var answerRows = await db.AttemptAnswers
            .Include(a => a.Attempt)
            .Include(a => a.ExamQuestion!).ThenInclude(eq => eq.Question)
            .Where(a => a.Attempt!.Status == AttemptStatus.Submitted)
            .ToListAsync();

        var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);

        var byChapter = answerRows
            .GroupBy(a => a.ExamQuestion!.Question!.Chapter)
            .Select(g => new ChapterAccuracyDto(
                Chapter: g.Key,
                ChapterTitle: chapterTitles.GetValueOrDefault(g.Key, g.Key),
                QuestionsSeen: g.Count(),
                Correct: g.Count(a => a.IsCorrect),
                AccuracyPct: g.Count() > 0 ? Math.Round(100m * g.Count(a => a.IsCorrect) / g.Count(), 1) : 0))
            .OrderBy(c => c.Chapter)
            .ToList();

        var byTopic = answerRows
            .Where(a => a.ExamQuestion!.Question!.Topic != null)
            .GroupBy(a => a.ExamQuestion!.Question!.Topic!)
            .Select(g => new TopicAccuracyDto(
                Topic: g.Key,
                QuestionsSeen: g.Count(),
                Correct: g.Count(a => a.IsCorrect),
                AccuracyPct: g.Count() > 0 ? Math.Round(100m * g.Count(a => a.IsCorrect) / g.Count(), 1) : 0))
            .OrderBy(t => t.AccuracyPct)
            .ToList();

        var weakestChapters = byChapter.OrderBy(c => c.AccuracyPct).Take(3).Select(c => c.Chapter).ToList();

        var trend = submittedAttempts
            .Select(a => new TrendPointDto(a.Id, a.SubmittedAtUtc, Math.Round(a.PercentScore, 1)))
            .ToList();

        return new AnalysisOverviewDto(overall, byChapter, byTopic, weakestChapters, trend);
    }

    /// Chapter code -> accuracy percent (0-100), only for chapters with at least one answered question so far.
    public async Task<Dictionary<string, decimal>> GetChapterAccuracyMapAsync()
    {
        var overview = await GetOverviewAsync();
        return overview.ByChapter.ToDictionary(c => c.Chapter, c => c.AccuracyPct);
    }
}
