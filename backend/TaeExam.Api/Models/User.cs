namespace TaeExam.Api.Models;

public enum UserStatus
{
    Active,
    Disabled,
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }

    // Profile fields (个人中心-资料修改) — all optional, filled in after registration.
    public string? RealName { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }

    public Role? Role { get; set; }
}
