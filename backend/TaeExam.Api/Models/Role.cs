namespace TaeExam.Api.Models;

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
