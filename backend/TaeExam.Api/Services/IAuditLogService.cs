namespace TaeExam.Api.Services;

public interface IAuditLogService
{
    // userIdOverride lets callers log an action for a not-yet-authenticated principal
    // (e.g. a failed login attempt, where HttpContext.User carries no claims yet).
    void Log(HttpContext httpContext, string action, string? entity = null, int? entityId = null, string? description = null, int? userIdOverride = null);
}
