namespace Aegis.Application.Abstractions;

public enum SecurityAuditAction
{
    Login,
    Logout,
    RefreshRotation,
    RefreshTokenReuse,
    PasswordChange,
    UserDeactivation
}

public enum SecurityAuditResult
{
    Success,
    FamilyRevoked
}

/// <summary>
/// Structured security audit data. Only explicitly safe identifiers and
/// categorical values may be sent to the audit sink.
/// </summary>
public sealed record SecurityAuditEvent(
    SecurityAuditAction Action,
    Guid? UserId,
    SecurityAuditResult Result,
    Guid? SessionId = null);
