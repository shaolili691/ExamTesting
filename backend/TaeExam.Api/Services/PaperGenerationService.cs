using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class PaperGenerationService(AppDbContext db, AnalysisService analysis)
{
    private const decimal DefaultPassFraction = 0.65m; // matches the most rigorous imported exam's bar

    public async Task<(Exam Exam, List<ChapterBlueprintDto> Blueprint, List<string> Warnings)> GenerateAsync(GenerateExamRequest req, int callerUserId)
    {
        var warnings = new List<string>();

        var chapters = await db.SyllabusChapters.OrderBy(c => c.Number).ToListAsync();
        var questionsByChapter = await db.Questions.ToListAsync();
        var countByChapter = questionsByChapter
            .GroupBy(q => q.Chapter)
            .ToDictionary(g => g.Key, g => g.Count());
        var totalQuestionsInBank = countByChapter.Values.Sum();

        // 1. Base blueprint: live per-chapter share of the question bank.
        var baseFractions = chapters.ToDictionary(
            c => c.Code,
            c => totalQuestionsInBank > 0 ? countByChapter.GetValueOrDefault(c.Code, 0) / (decimal)totalQuestionsInBank : 0m);

        // 2. Weak-chapter boost, using aggregate accuracy across all submitted attempts so far.
        var weightedFractions = new Dictionary<string, decimal>(baseFractions);
        if (req.BoostWeakChapters)
        {
            var accuracyByChapter = await analysis.GetChapterAccuracyMapAsync(callerUserId);
            if (accuracyByChapter.Count > 0)
            {
                var overallAvgAccuracy = accuracyByChapter.Values.Average();
                foreach (var chapter in chapters)
                {
                    if (accuracyByChapter.TryGetValue(chapter.Code, out var acc) && acc < overallAvgAccuracy)
                    {
                        weightedFractions[chapter.Code] *= req.BoostFactor;
                    }
                    // chapters with no attempt history yet are treated as exactly average: no boost, no penalty.
                }
            }

            var sum = weightedFractions.Values.Sum();
            if (sum > 0)
            {
                foreach (var key in weightedFractions.Keys.ToList())
                {
                    weightedFractions[key] /= sum;
                }
            }
        }

        // 3. Target counts via largest-remainder method, then clamp to availability.
        var targetCounts = LargestRemainderAllocate(weightedFractions, req.QuestionCount);
        var clamped = new Dictionary<string, int>();
        var shortfall = 0;
        foreach (var (chapter, target) in targetCounts)
        {
            var available = countByChapter.GetValueOrDefault(chapter, 0);
            var take = Math.Min(target, available);
            clamped[chapter] = take;
            shortfall += target - take;
        }

        if (shortfall > 0)
        {
            var headroomOrder = weightedFractions
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();
            var guard = 0;
            while (shortfall > 0 && guard < 10_000)
            {
                var progressedThisPass = false;
                foreach (var chapter in headroomOrder)
                {
                    if (shortfall <= 0) break;
                    var available = countByChapter.GetValueOrDefault(chapter, 0);
                    if (clamped[chapter] < available)
                    {
                        clamped[chapter]++;
                        shortfall--;
                        progressedThisPass = true;
                    }
                }
                guard++;
                if (!progressedThisPass) break; // bank is fully exhausted, cannot reach requested count
            }
            if (shortfall > 0)
            {
                warnings.Add($"Question bank only has {totalQuestionsInBank} questions; could not reach the requested {req.QuestionCount} (short by {shortfall}).");
            }
        }

        // 4. Selection with repeat-avoidance (scoped to this user's own recent exams only,
        // so one user's history never leaks into another's blueprint).
        var recentExamIds = await db.Exams
            .Where(e => e.UserId == callerUserId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(req.AvoidRecentExamsCount)
            .Select(e => e.Id)
            .ToListAsync();
        var recentlyUsedQuestionIds = recentExamIds.Count == 0
            ? new HashSet<int>()
            : (await db.ExamQuestions.Where(eq => recentExamIds.Contains(eq.ExamId)).Select(eq => eq.QuestionId).ToListAsync()).ToHashSet();

        var questionsByChapterLookup = questionsByChapter.ToLookup(q => q.Chapter);
        var rng = Random.Shared;
        var selected = new List<Question>();

        foreach (var (chapter, count) in clamped)
        {
            if (count <= 0) continue;
            var pool = questionsByChapterLookup[chapter].ToList();
            var fresh = pool.Where(q => !recentlyUsedQuestionIds.Contains(q.Id)).OrderBy(_ => rng.Next()).ToList();
            var reused = pool.Where(q => recentlyUsedQuestionIds.Contains(q.Id)).OrderBy(_ => rng.Next()).ToList();

            var picks = fresh.Take(count).ToList();
            if (picks.Count < count)
            {
                var need = count - picks.Count;
                var extra = reused.Take(need).ToList();
                picks.AddRange(extra);
                if (extra.Count > 0)
                {
                    warnings.Add($"{chapter}: only {fresh.Count} fresh question(s) available, reused {extra.Count} from a recent exam.");
                }
            }
            selected.AddRange(picks);
        }

        // 5. Assemble: shuffle final order so chapters aren't grouped, then persist.
        selected = selected.OrderBy(_ => rng.Next()).ToList();
        var totalPoints = selected.Sum(q => q.Points);
        var passThreshold = (int)Math.Ceiling(totalPoints * DefaultPassFraction);

        var blueprintUsed = clamped
            .Select(kv => new ChapterBlueprintDto(kv.Key, kv.Value, Math.Round(weightedFractions.GetValueOrDefault(kv.Key, 0), 3)))
            .OrderBy(b => b.Chapter)
            .ToList();

        var tenMinChapterRatio = 2.25; // minutes/question, matching the original mock exams' 90min/40q ratio
        var timeLimitMinutes = (int)Math.Round(selected.Count * tenMinChapterRatio);

        var exam = new Exam
        {
            Title = req.Title ?? $"Generated exam — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            Type = ExamType.Generated,
            BlueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprintUsed),
            TotalPoints = totalPoints,
            PassThresholdPoints = passThreshold,
            TimeLimitMinutes = timeLimitMinutes,
            CategoryId = 1, // TAE — the only category that exists today
            UserId = callerUserId,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var order = 0;
        foreach (var q in selected)
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

        return (exam, blueprintUsed, warnings);
    }

    private static Dictionary<string, int> LargestRemainderAllocate(Dictionary<string, decimal> fractions, int total)
    {
        var raw = fractions.ToDictionary(kv => kv.Key, kv => kv.Value * total);
        var floors = raw.ToDictionary(kv => kv.Key, kv => (int)Math.Floor(kv.Value));
        var remainders = raw.ToDictionary(kv => kv.Key, kv => kv.Value - floors[kv.Key]);
        var allocated = floors.Values.Sum();
        var remaining = total - allocated;

        foreach (var key in remainders.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(Math.Max(0, remaining)))
        {
            floors[key]++;
        }
        return floors;
    }
}
