namespace TaeExam.Api.Models;

public class SyllabusChapter
{
    public string Code { get; set; } = ""; // "Ch1".."Ch8", primary key
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public int StudyMinutes { get; set; }
    public string KLevel { get; set; } = "";
}
