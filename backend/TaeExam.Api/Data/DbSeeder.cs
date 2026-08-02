using System.Text.Json;
using System.Text.Json.Serialization;
using TaeExam.Api.Models;

namespace TaeExam.Api.Data;

public static class DbSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void Seed(AppDbContext db, string seedDataPath)
    {
        // Roles and ExamCategory each get their own independent guard, not the Questions guard below —
        // otherwise a future partially-seeded database (Questions already populated) would silently
        // never seed Roles/Categories.
        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new Role { Name = RoleNames.Administrator },
                new Role { Name = RoleNames.Teacher },
                new Role { Name = RoleNames.Student });
            db.SaveChanges();
        }

        if (!db.ExamCategories.Any())
        {
            db.ExamCategories.Add(new ExamCategory { Name = "TAE" });
            db.SaveChanges();
        }

        {
            // Per-key upsert (not a single "table empty" guard) so that a permission added after
            // go-live — e.g. question:manage — still gets its default role assignment seeded into
            // an already-running database instead of silently having no roles granted anywhere.
            var allRoles = new[] { RoleNames.Administrator, RoleNames.Teacher, RoleNames.Student };
            var adminAndTeacher = new[] { RoleNames.Administrator, RoleNames.Teacher };
            var adminOnly = new[] { RoleNames.Administrator };

            var defaults = new Dictionary<string, string[]>
            {
                ["exam:start"] = allRoles,
                ["exam:submit"] = allRoles,
                ["exam:review"] = allRoles,
                ["paper:create"] = allRoles,
                ["analysis:view"] = allRoles,
                ["wrongquestion:view"] = allRoles,
                ["announcement:view"] = allRoles,
                ["exam:import"] = adminAndTeacher,
                ["question:manage"] = adminAndTeacher,
                ["announcement:manage"] = adminOnly,
                ["user:view"] = adminOnly,
                ["user:manage"] = adminOnly,
                ["audit:view"] = adminOnly,
                ["permission:manage"] = adminOnly,
            };

            var existing = db.RolePermissions.Select(rp => rp.PermissionKey + "|" + rp.RoleName).ToHashSet();
            var added = false;
            foreach (var (key, roles) in defaults)
            {
                foreach (var role in roles)
                {
                    if (existing.Add(key + "|" + role))
                    {
                        db.RolePermissions.Add(new RolePermission { PermissionKey = key, RoleName = role });
                        added = true;
                    }
                }
            }
            if (added) db.SaveChanges();
        }

        // Bootstrap: a fresh database has no way to reach Administrator-only endpoints (user
        // management, audit log, announcements) without one seeded admin account to start from.
        if (!db.Users.Any(u => u.RoleId == db.Roles.First(r => r.Name == RoleNames.Administrator).Id))
        {
            var adminRoleId = db.Roles.First(r => r.Name == RoleNames.Administrator).Id;
            db.Users.Add(new User
            {
                Username = "admin",
                Email = "admin@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@12345", workFactor: 11),
                RoleId = adminRoleId,
                Status = UserStatus.Active,
            });
            db.SaveChanges();
        }

        if (db.Questions.Any())
        {
            return; // already seeded
        }

        var chapters = ReadJson<List<SeedChapter>>(Path.Combine(seedDataPath, "syllabus_chapters.json"));
        foreach (var c in chapters)
        {
            db.SyllabusChapters.Add(new SyllabusChapter
            {
                Code = c.Code,
                Number = c.Number,
                Title = c.Title,
                StudyMinutes = c.StudyMinutes,
                KLevel = c.KLevel,
            });
        }
        db.SaveChanges();

        var seedQuestions = ReadJson<List<SeedQuestion>>(Path.Combine(seedDataPath, "questions.json"));
        var legacyIdToQuestionId = new Dictionary<(string sourceFile, int legacyId), int>();

        foreach (var q in seedQuestions.OrderBy(q => q.ImportOrder))
        {
            var entity = new Question
            {
                LegacyId = q.LegacyId,
                SourceFile = q.SourceFile,
                Chapter = q.Chapter,
                Topic = q.Topic,
                Level = q.Level,
                IsMultiChoice = q.IsMultiChoice,
                IsScenario = q.IsScenario,
                ScenarioText = q.ScenarioText,
                QuestionText = q.QuestionText,
                Options = q.Options,
                CorrectIndexes = q.CorrectIndexes,
                DistractorDesign = q.DistractorDesign,
                Explanation = q.Explanation,
                Points = q.Points,
            };
            db.Questions.Add(entity);
            db.SaveChanges(); // ensures entity.Id is populated for the lookup map
            legacyIdToQuestionId[(q.SourceFile, q.LegacyId)] = entity.Id;
        }

        var taeCategoryId = db.ExamCategories.First(c => c.Name == "TAE").Id;
        var importedExams = ReadJson<List<SeedImportedExam>>(Path.Combine(seedDataPath, "imported_exams.json"));
        foreach (var ex in importedExams)
        {
            var exam = new Exam
            {
                Title = ex.Title,
                Type = ExamType.Imported,
                SourceFile = ex.SourceFile,
                TotalPoints = ex.TotalPoints,
                PassThresholdPoints = ex.PassThresholdPoints,
                TimeLimitMinutes = (int)Math.Round(ex.QuestionLegacyIdsInOrder.Count * 2.25), // matches the ~90min/40q ratio of the original mock exams
                CategoryId = taeCategoryId,
                UserId = null, // shared/global — visible to every user
            };
            db.Exams.Add(exam);
            db.SaveChanges();

            var order = 0;
            foreach (var legacyId in ex.QuestionLegacyIdsInOrder)
            {
                var questionId = legacyIdToQuestionId[(ex.SourceFile, legacyId)];
                var question = db.Questions.Find(questionId)!;
                db.ExamQuestions.Add(new ExamQuestion
                {
                    ExamId = exam.Id,
                    QuestionId = questionId,
                    OrderIndex = order++,
                    PointsOverride = question.Points,
                });
            }
            db.SaveChanges();
        }
    }

    private static T ReadJson<T>(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(text, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to parse seed file: {path}");
    }

    private class SeedChapter
    {
        public string Code { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public int StudyMinutes { get; set; }
        public string KLevel { get; set; } = "";
    }

    private class SeedQuestion
    {
        public int ImportOrder { get; set; }
        public int LegacyId { get; set; }
        public string SourceFile { get; set; } = "";
        public string Chapter { get; set; } = "";
        public string? Topic { get; set; }
        public string? Level { get; set; }
        public bool IsMultiChoice { get; set; }
        public bool IsScenario { get; set; }
        public string? ScenarioText { get; set; }
        public string QuestionText { get; set; } = "";
        public List<string> Options { get; set; } = new();
        public List<int> CorrectIndexes { get; set; } = new();
        public string? DistractorDesign { get; set; }
        public string Explanation { get; set; } = "";
        public int Points { get; set; }
    }

    private class SeedImportedExam
    {
        public string SourceFile { get; set; } = "";
        public string Title { get; set; } = "";
        public int TotalPoints { get; set; }
        public int PassThresholdPoints { get; set; }
        public List<int> QuestionLegacyIdsInOrder { get; set; } = new();
    }
}
