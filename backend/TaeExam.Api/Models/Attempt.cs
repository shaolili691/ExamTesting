namespace TaeExam.Api.Models;

public enum AttemptStatus
{
    InProgress,
    Submitted,
}

public class Attempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int UserId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public decimal PercentScore { get; set; }
    public bool Passed { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

    public Exam? Exam { get; set; }
    public List<AttemptAnswer> Answers { get; set; } = new();
}
