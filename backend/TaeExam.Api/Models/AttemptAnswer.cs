namespace TaeExam.Api.Models;

public class AttemptAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public int ExamQuestionId { get; set; }
    public List<int> SelectedIndexes { get; set; } = new(); // empty = unanswered
    public bool IsCorrect { get; set; }
    public int PointsAwarded { get; set; }
    public bool Marked { get; set; }

    public Attempt? Attempt { get; set; }
    public ExamQuestion? ExamQuestion { get; set; }
}
