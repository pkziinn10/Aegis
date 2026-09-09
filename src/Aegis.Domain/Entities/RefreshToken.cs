using Aegis.Domain.Results;

namespace Aegis.Domain.Entities;

public sealed class RefreshToken
{
    public RefreshToken(Guid id, Guid sessionId, string hash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : this(id, sessionId, hash, createdAt, expiresAt, null) { }

    private RefreshToken(Guid id, Guid sessionId, string hash, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, DateTimeOffset? revokedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Hash inválido.", nameof(hash));
        if (expiresAt <= createdAt) throw new ArgumentException("Refresh deve expirar depois da criação.", nameof(expiresAt));
        if (revokedAt is not null && revokedAt < createdAt)
            throw new ArgumentException("Data de revogação incoerente.", nameof(revokedAt));
        Id = id; SessionId = sessionId; Hash = hash; CreatedAt = createdAt; ExpiresAt = expiresAt; RevokedAt = revokedAt;
    }

    public Guid Id { get; }
    public Guid SessionId { get; }
    public string Hash { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now >= CreatedAt && !IsExpired(now);

    public static RefreshToken Rehydrate(Guid id, Guid sessionId, string hash, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, DateTimeOffset? revokedAt) =>
        new(id, sessionId, hash, createdAt, expiresAt, revokedAt);

    internal DomainResult Revoke(DateTimeOffset now)
    {
        if (now < CreatedAt) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        RevokedAt ??= now;
        return DomainResult.Success();
    }
}
