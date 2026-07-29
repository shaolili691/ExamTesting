namespace TaeExam.Api.Models;

public class RolePermission
{
    public int Id { get; set; }
    public string PermissionKey { get; set; } = "";
    public string RoleName { get; set; } = "";
}
