using System.Threading.Channels;
using TaeExam.Api.Dtos;

namespace TaeExam.Api.Services;

// Singleton. Request handlers enqueue synchronously (non-blocking); AuditLogWriter drains
// it on a background thread so audit logging can never slow down or fail a real request.
public class AuditLogChannel
{
    private readonly Channel<AuditLogEntry> _channel =
        Channel.CreateUnbounded<AuditLogEntry>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<AuditLogEntry> Reader => _channel.Reader;

    public void Enqueue(AuditLogEntry entry) => _channel.Writer.TryWrite(entry);
}
