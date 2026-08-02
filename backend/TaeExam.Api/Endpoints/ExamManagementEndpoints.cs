using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Authorization;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class ExamManagementEndpoints
{
    public static void MapExamManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/exam-management").RequirePermission("question:manage");

        // Shared/global exams only (UserId == null): the 6 seeded "Imported" exams plus any
        // PDF-imported "Manual" ones. Never the private per-student Generated/Drill practice
        // papers — see design notes in "exam management.md".
        group.MapGet("/exams", async (AppDbContext db) =>
        {
            var exams = await db.Exams
                .Include(e => e.ExamQuestions)
                .Where(e => e.UserId == null)
                .OrderByDescending(e => e.CreatedAtUtc)
                .ToListAsync();

            var dtos = exams
                .Select(e => new ManagedExamDto(e.Id, e.Title, e.Type.ToString(), e.SourceFile, e.ExamQuestions.Count, e.TotalPoints, e.CreatedAtUtc))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/exams/{examId:int}/questions", async (int examId, AppDbContext db) =>
        {
            var exam = await db.Exams
                .Include(e => e.ExamQuestions.OrderBy(eq => eq.OrderIndex)).ThenInclude(eq => eq.Question)
                .FirstOrDefaultAsync(e => e.Id == examId && e.UserId == null);
            if (exam is null) return Results.NotFound();

            var chapterTitles = await db.SyllabusChapters.ToDictionaryAsync(c => c.Code, c => c.Title);
            var questionIds = exam.ExamQuestions.Select(eq => eq.QuestionId).ToList();

            // Which OTHER shared exams also reference each question — drives the "this question
            // also appears in: ..." warning so an editor doesn't think the edit is scoped to just
            // the exam they're currently looking at.
            var usageRows = await db.ExamQuestions
                .Where(eq => questionIds.Contains(eq.QuestionId) && eq.ExamId != examId)
                .Join(db.Exams.Where(e => e.UserId == null), eq => eq.ExamId, e => e.Id, (eq, e) => new { eq.QuestionId, e.Title })
                .ToListAsync();
            var usageByQuestion = usageRows
                .GroupBy(u => u.QuestionId)
                .ToDictionary(g => g.Key, g => g.Select(u => u.Title).Distinct().ToList());

            var questions = exam.ExamQuestions.Select(eq =>
            {
                var q = eq.Question!;
                return new QuestionAdminDto(
                    q.Id,
                    eq.OrderIndex,
                    q.Chapter,
                    chapterTitles.GetValueOrDefault(q.Chapter),
                    q.Topic,
                    q.Level,
                    q.IsScenario,
                    q.ScenarioText,
                    q.QuestionText,
                    q.Options,
                    q.IsMultiChoice,
                    q.CorrectIndexes,
                    q.Explanation,
                    q.Points,
                    usageByQuestion.GetValueOrDefault(q.Id, []));
            }).ToList();

            return Results.Ok(new ManagedExamQuestionsResponse(exam.Id, exam.Title, questions));
        });

        // Deliberately keyed by QuestionId, not ExamId+index: correct answers/explanations live
        // on the shared Question row, so this fixes the question everywhere it's used at once —
        // see the change-impact note in "exam management.md" section 3.
        group.MapPut("/questions/{questionId:int}", async (int questionId, UpdateQuestionAnswerRequest req, AppDbContext db, IAuditLogService audit, HttpContext http) =>
        {
            var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId);
            if (question is null) return Results.NotFound();

            if (req.CorrectIndexes.Count == 0)
                return Results.BadRequest(new { error = "At least one correct answer must be selected." });
            if (req.CorrectIndexes.Any(i => i < 0 || i >= question.Options.Count))
                return Results.BadRequest(new { error = "Correct answer index out of range." });
            if (req.CorrectIndexes.Distinct().Count() != req.CorrectIndexes.Count)
                return Results.BadRequest(new { error = "Duplicate correct answer index." });
            if (!question.IsMultiChoice && req.CorrectIndexes.Count != 1)
                return Results.BadRequest(new { error = "This is a single-choice question — exactly one correct answer is required." });
            if (string.IsNullOrWhiteSpace(req.Explanation))
                return Results.BadRequest(new { error = "Explanation is required." });

            var before = string.Join(",", question.CorrectIndexes);
            question.CorrectIndexes = req.CorrectIndexes.OrderBy(i => i).ToList();
            question.Explanation = req.Explanation.Trim();
            await db.SaveChangesAsync();

            audit.Log(http, "UpdateQuestion", "Question", questionId, $"Correct answer {before} -> {string.Join(",", question.CorrectIndexes)}");
            return Results.Ok();
        });
    }
}
