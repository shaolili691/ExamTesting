using System.Security.Claims;
using TaeExam.Api.Dtos;

namespace TaeExam.Api.Services;

public class AuditLogService(AuditLogChannel channel) : IAuditLogService
{
    public void Log(HttpContext httpContext, string action, string? entity = null, int? entityId = null, string? description = null, int? userIdOverride = null)
    {
        int? userId = userIdOverride;
        if (userId is null)
        {
            var claim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            userId = claim is not null ? int.Parse(claim) : null;
        }

        var ip = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString();
        var browser = httpContext.Request.Headers["User-Agent"].FirstOrDefault();

        channel.Enqueue(new AuditLogEntry(userId, action, entity, entityId, description, ip, browser, DateTime.UtcNow));
    }
}
