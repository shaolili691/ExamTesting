namespace TaeExam.Api.Models;

public class ExamQuestion
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public int PointsOverride { get; set; }

    public Exam? Exam { get; set; }
    public Question? Question { get; set; }
}
