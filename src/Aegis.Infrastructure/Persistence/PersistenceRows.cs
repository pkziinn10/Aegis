namespace Aegis.Infrastructure.Persistence;

public sealed class UserRow { public Guid Id { get; set; } public string Email { get; set; } = string.Empty; public string PasswordHash { get; set; } = string.Empty; public int Role { get; set; } public bool IsActive { get; set; } public long Version { get; set; } }
public sealed class SessionRow { public Guid Id { get; set; } public Guid UserId { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset ExpiresAt { get; set; } public DateTimeOffset? RevokedAt { get; set; } public int? RevocationReason { get; set; } public long Version { get; set; } public List<RefreshTokenRow> RefreshTokens { get; set; } = []; }
public sealed class RefreshTokenRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public string Hash { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset ExpiresAt { get; set; } public DateTimeOffset? RevokedAt { get; set; } }
public sealed class AuditEventRow { public Guid Id { get; set; } = Guid.NewGuid(); public Guid? UserId { get; set; } public string Action { get; set; } = string.Empty; public string? MetadataJson { get; set; } public DateTimeOffset CreatedAt { get; set; } }

