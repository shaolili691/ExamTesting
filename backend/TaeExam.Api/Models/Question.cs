namespace TaeExam.Api.Models;

public class Question
{
    public int Id { get; set; }
    public int LegacyId { get; set; }
    public string SourceFile { get; set; } = "";
    public string Chapter { get; set; } = ""; // FK -> SyllabusChapter.Code
    public string? Topic { get; set; }
    public string? Level { get; set; } // K2/K3/K4, nullable
    public bool IsMultiChoice { get; set; }
    public bool IsScenario { get; set; }
    public string? ScenarioText { get; set; }
    public string QuestionText { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public List<int> CorrectIndexes { get; set; } = new();
    public string? DistractorDesign { get; set; }
    public string Explanation { get; set; } = "";
    public int Points { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public SyllabusChapter? ChapterRef { get; set; }
}
