using System.Text.Json;
using Aegis.Application.Abstractions;

namespace Aegis.Infrastructure.Persistence;

public sealed class AuditWriter(AegisDbContext db, IClock clock) : IAuditWriter
{
    private static readonly HashSet<string> Allowed = ["reason", "result", "ip", "userAgent", "sessionId", "keyId"];
    public async Task WriteAsync(string action, Guid? userId, IReadOnlyDictionary<string, string?>? metadata = null, CancellationToken cancellationToken = default)
    {
        var safe = metadata?.Where(x => Allowed.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        db.AuditEvents.Add(new AuditEventRow { Action = action, UserId = userId, MetadataJson = safe is null ? null : JsonSerializer.Serialize(safe), CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }
}
