using System.Text.Json;
using Aegis.Application.Abstractions;

namespace Aegis.Infrastructure.Persistence;

public sealed class AuditWriter(AegisDbContext db, IClock clock) : IAuditWriter
{
    public Task WriteAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var metadata = new Dictionary<string, string?>
        {
            ["result"] = auditEvent.Result switch
            {
                SecurityAuditResult.Success => "success",
                SecurityAuditResult.FamilyRevoked => "family_revoked",
                _ => throw new ArgumentOutOfRangeException(nameof(auditEvent), auditEvent.Result, "Unsupported audit result.")
            }
        };
        if (auditEvent.SessionId is Guid sessionId) metadata["sessionId"] = sessionId.ToString("N");

        db.AuditEvents.Add(new AuditEventRow
        {
            Action = auditEvent.Action switch
            {
                SecurityAuditAction.Login => "login",
                SecurityAuditAction.Logout => "logout",
                SecurityAuditAction.RefreshRotation => "refresh_rotation",
                SecurityAuditAction.RefreshTokenReuse => "refresh_token_reuse",
                SecurityAuditAction.PasswordChange => "password_change",
                SecurityAuditAction.UserDeactivation => "user_deactivation",
                _ => throw new ArgumentOutOfRangeException(nameof(auditEvent), auditEvent.Action, "Unsupported audit action.")
            },
            UserId = auditEvent.UserId,
            MetadataJson = JsonSerializer.Serialize(metadata),
            CreatedAt = clock.UtcNow
        });
        return Task.CompletedTask;
    }
}
