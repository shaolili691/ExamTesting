namespace TaeExam.Api.Models;

public enum ExamType
{
    Imported,
    Generated,
    Drill,
    Manual,
}

public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public ExamType Type { get; set; }
    public string? SourceFile { get; set; } // Imported only
    public int? GeneratedFromAttemptId { get; set; } // Drill only
    public string? BlueprintJson { get; set; } // Generated only: per-chapter target counts/weights used, for audit
    public int? CategoryId { get; set; }
    public int? UserId { get; set; } // null = shared/global (Imported); set = private to that user (Generated/Drill)
    public int TotalPoints { get; set; }
    public int PassThresholdPoints { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ExamQuestion> ExamQuestions { get; set; } = new();
}
