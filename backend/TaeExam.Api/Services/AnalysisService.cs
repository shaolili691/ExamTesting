using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class AnalysisService(AppDbContext db)
{
    /// userId == null means "all users" — callers must only pass null when the caller is an Administrator
    /// who explicitly asked for the cross-user view; every other caller must pass their own id.
    public async Task<AnalysisOverviewDto> GetOverviewAsync(int? userId)
    {
        var attemptsQuery = db.Attempts.Where(a => a.Status == AttemptStatus.Submitted);
        if (userId.HasValue) attemptsQuery = attemptsQuery.Where(a => a.UserId == userId.Value);

        var submittedAttempts = await attemptsQuery.OrderBy(a => a.SubmittedAtUtc).ToListAsync();

        var studyTimeMinutes = submittedAttempts
            .Where(a => a.SubmittedAtUtc.HasValue)
            .Sum(a => (a.SubmittedAtUtc!.Value - a.StartedAtUtc).TotalMinutes);
        var completedExams = submittedAttempts.Select(a => a.ExamId).Distinct().Count();

        var overall = new OverallStatsDto(
            TotalAttempts: submittedAttempts.Count,
            AvgPercent: submittedAttempts.Count > 0 ? Math.Round(submittedAttempts.Average(a => a.PercentScore), 1) : 0,
            PassRate: submittedAttempts.Count > 0 ? Math.Round(100m * submittedAttempts.Count(a => a.Passed) / submittedAttempts.Count, 1) : 0,
            StudyTimeMinutes: (int)Math.Round(studyTimeMinutes),
            CompletedExams: completedExams);

        var attemptIds = submittedAttempts.Select(a => a.Id).ToHashSet();
        var answerRows = await db.AttemptAnswers
            .Include(a => a.ExamQuestion!).ThenInclude(eq => eq.Question)
            .Where(a => attemptIds.Contains(a.AttemptId))
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

    /// Chapter code -> accuracy percent (0-100), only for chapters with at least one answered question so far,
    /// scoped to one user (used by PaperGenerationService to boost that user's own weak chapters).
    public async Task<Dictionary<string, decimal>> GetChapterAccuracyMapAsync(int userId)
    {
        var overview = await GetOverviewAsync(userId);
        return overview.ByChapter.ToDictionary(c => c.Chapter, c => c.AccuracyPct);
    }
}
