using TaeExam.Api.Data;
using TaeExam.Api.Models;

namespace TaeExam.Api.Services;

public class AuditLogWriter(AuditLogChannel channel, IServiceScopeFactory scopeFactory, ILogger<AuditLogWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AuditLogs.Add(new AuditLog
                {
                    UserId = entry.UserId,
                    Action = entry.Action,
                    Entity = entry.Entity,
                    EntityId = entry.EntityId,
                    Description = entry.Description,
                    Ip = entry.Ip,
                    Browser = entry.Browser,
                    CreatedAtUtc = entry.CreatedAtUtc,
                });
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist audit log entry for action {Action}", entry.Action);
            }
        }
    }
}
