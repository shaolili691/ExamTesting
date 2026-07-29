namespace TaeExam.Api.Models;

public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime PublishAtUtc { get; set; }
    public DateTime? ExpireAtUtc { get; set; }
    public int Priority { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? CreatedByUser { get; set; }
}
