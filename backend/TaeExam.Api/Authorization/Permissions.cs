namespace TaeExam.Api.Authorization;

public static class Permissions
{
    // Catalog of known permission keys and their human-readable labels, shown on the
    // role-permissions admin screen. The actual role -> permission assignments live in the
    // RolePermissions table (see RolePermissionsEndpoints) so admins can edit them at runtime.
    public static readonly (string Key, string Label)[] Catalog =
    [
        ("exam:start", "Start/resume exam attempts"),
        ("exam:submit", "Submit exam attempts"),
        ("exam:review", "Review exam history"),
        ("paper:create", "Generate practice papers/drills"),
        ("exam:import", "Import exams"),
        ("question:manage", "Manage exam questions (correct answers & explanations)"),
        ("analysis:view", "View analysis dashboard"),
        ("wrongquestion:view", "View wrong-question bank"),
        ("announcement:view", "View announcements"),
        ("announcement:manage", "Manage announcements"),
        ("user:view", "View users"),
        ("user:manage", "Manage users"),
        ("audit:view", "View audit log"),
        ("permission:manage", "Manage role permissions"),
    ];
}
