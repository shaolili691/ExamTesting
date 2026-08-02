using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class DrillGenerationService(AppDbContext db, WrongQuestionService wrongQuestions)
{
    private const decimal DefaultPassFraction = 0.65m;

    public async Task<(Exam Exam, int CoreCount, int FillCount, List<string> Warnings)> GenerateFromWrongBookAsync(int callerUserId, DrillRequest req)
    {
        var open = await wrongQuestions.GetOpenWrongQuestionsAsync(callerUserId);
        if (open.Count == 0)
        {
            throw new InvalidOperationException("Your wrong-question book is empty — nothing to practice.");
        }

        var coreQuestions = open.Select(o => o.Question).ToList();
        var (exam, fillCount, warnings) = await BuildDrillExamAsync(
            coreQuestions, req, callerUserId, "Practice: full wrong-question book", generatedFromAttemptId: null);

        return (exam, coreQuestions.Count, fillCount, warnings);
    }

    public async Task<(Exam Exam, List<ChapterAccuracyDto> WeakAreaSummary, int CoreCount, int FillCount, List<string> Warnings)> GenerateAsync(int attemptId, DrillRequest req, int callerUserId, bool isAdmin)
    {
        var warnings = new List<string>();

        var attempt = await db.Attempts
            .Include(a => a.Exam!).ThenInclude(e => e.ExamQuestions)
            .Include(a => a.Answers).ThenInclude(ans => ans.ExamQuestion!).ThenInclude(eq => eq.Question)
            .FirstOrDefaultAsync(a => a.Id == attemptId)
            ?? throw new InvalidOperationException("Attempt not found");

        if (attempt.UserId != callerUserId && !isAdmin)
        {
            throw new UnauthorizedAccessException("You do not own this attempt");
        }

        if (attempt.Status != AttemptStatus.Submitted)
        {
            throw new InvalidOperationException("Attempt must be submitted before a drill can be generated from it");
        }

        var sourceQuestionIds = attempt.Exam!.ExamQuestions.Select(eq => eq.QuestionId).ToHashSet();

        var coreAnswers = attempt.Answers.Where(a => !a.IsCorrect).ToList();
        var coreQuestions = coreAnswers.Select(a => a.ExamQuestion!.Question!).ToList();

        var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);

        // Weak-area summary is computed from the source attempt only, driving the drill page's banner.
        var weakAreaSummary = attempt.Answers
            .GroupBy(a => a.ExamQuestion!.Question!.Chapter)
            .Select(g => new ChapterAccuracyDto(
                Chapter: g.Key,
                ChapterTitle: chapterTitles.GetValueOrDefault(g.Key, g.Key),
                QuestionsSeen: g.Count(),
                Correct: g.Count(a => a.IsCorrect),
                AccuracyPct: g.Count() > 0 ? Math.Round(100m * g.Count(a => a.IsCorrect) / g.Count(), 1) : 0))
            .OrderBy(c => c.AccuracyPct)
            .ToList();

        if (coreQuestions.Count == 0)
        {
            throw new InvalidOperationException("This attempt has no wrong or unanswered questions — nothing to drill.");
        }

        var (exam, fillCount, buildWarnings) = await BuildDrillExamAsync(
            coreQuestions, req, callerUserId, $"Targeted drill from attempt #{attemptId}", generatedFromAttemptId: attemptId, excludeQuestionIds: sourceQuestionIds);
        warnings.AddRange(buildWarnings);

        return (exam, weakAreaSummary, coreQuestions.Count, fillCount, warnings);
    }

    // Shared by both drill flavors: picks fill-in questions from the same chapters/topics as the
    // core (missed) questions, then persists a new Drill exam.
    private async Task<(Exam Exam, int FillCount, List<string> Warnings)> BuildDrillExamAsync(
        List<Question> coreQuestions, DrillRequest req, int callerUserId, string title, int? generatedFromAttemptId, HashSet<int>? excludeQuestionIds = null)
    {
        var warnings = new List<string>();
        var excluded = excludeQuestionIds ?? coreQuestions.Select(q => q.Id).ToHashSet();

        var touchedChapterTopics = coreQuestions.Select(q => (q.Chapter, q.Topic)).Distinct().ToList();
        var touchedChapters = touchedChapterTopics.Select(t => t.Chapter).ToHashSet();

        var candidatePool = await db.Questions
            .Where(q => touchedChapters.Contains(q.Chapter) && !excluded.Contains(q.Id))
            .ToListAsync();

        bool SameTopicMatch(Question q) =>
            touchedChapterTopics.Any(t => t.Chapter == q.Chapter && t.Topic != null && t.Topic == q.Topic);

        var rng = Random.Shared;
        var rankedFill = candidatePool
            .Select(q => new { Question = q, Tier = SameTopicMatch(q) ? 0 : 1 })
            .OrderBy(x => x.Tier)
            .ThenByDescending(x => x.Question.IsScenario)
            .ThenBy(_ => rng.Next())
            .Select(x => x.Question)
            .ToList();

        var targetSize = req.MaxQuestions ?? Math.Clamp(coreQuestions.Count * 2, 10, 40);
        var fillNeeded = Math.Max(0, targetSize - coreQuestions.Count);
        var fillPicks = rankedFill.Take(fillNeeded).ToList();
        if (fillPicks.Count < fillNeeded)
        {
            warnings.Add($"Only {fillPicks.Count} fill question(s) available for the weak chapters/topics (wanted {fillNeeded}).");
        }

        var allPicks = coreQuestions.Concat(fillPicks).ToList();
        var totalPoints = allPicks.Sum(q => q.Points);
        var passThreshold = (int)Math.Ceiling(totalPoints * DefaultPassFraction);

        var timeLimitMinutes = (int)Math.Round(allPicks.Count * 2.25); // same min/question ratio as generated papers

        var exam = new Exam
        {
            Title = title,
            Type = ExamType.Drill,
            GeneratedFromAttemptId = generatedFromAttemptId,
            TotalPoints = totalPoints,
            PassThresholdPoints = passThreshold,
            TimeLimitMinutes = timeLimitMinutes,
            CategoryId = 1,
            UserId = callerUserId,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var order = 0;
        foreach (var q in allPicks)
        {
            db.ExamQuestions.Add(new ExamQuestion
            {
                ExamId = exam.Id,
                QuestionId = q.Id,
                OrderIndex = order++,
                PointsOverride = q.Points,
            });
        }
        await db.SaveChangesAsync();

        return (exam, fillPicks.Count, warnings);
    }
}
