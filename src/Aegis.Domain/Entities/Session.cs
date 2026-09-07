using Aegis.Domain.Enums;
using Aegis.Domain.Results;

namespace Aegis.Domain.Entities;

public sealed class Session
{
    private readonly List<RefreshToken> refreshTokens;

    public static Session Rehydrate(Guid id, Guid userId, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        IEnumerable<RefreshToken> refreshTokens, DateTimeOffset? revokedAt,
        SessionRevocationReason? revocationReason, long version) =>
        new(id, userId, createdAt, expiresAt, refreshTokens, revokedAt, revocationReason, version, true);

    public Session(Guid id, Guid userId, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        IEnumerable<RefreshToken> refreshTokens, DateTimeOffset? revokedAt = null,
        SessionRevocationReason? revocationReason = null, long version = 1)
        : this(id, userId, createdAt, expiresAt, refreshTokens, revokedAt, revocationReason, version, false) { }

    private Session(Guid id, Guid userId, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        IEnumerable<RefreshToken> refreshTokens, DateTimeOffset? revokedAt,
        SessionRevocationReason? revocationReason, long version, bool rehydrating)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (userId == Guid.Empty) throw new ArgumentException("Usuário inválido.", nameof(userId));
        if (expiresAt <= createdAt) throw new ArgumentException("Sessão deve expirar depois da criação.", nameof(expiresAt));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if ((revokedAt is null) != (revocationReason is null)) throw new ArgumentException("Revogação exige data e motivo.");
        if (revokedAt is not null && revokedAt < createdAt) throw new ArgumentException("Data de revogação incoerente.", nameof(revokedAt));
        Id = id; UserId = userId; CreatedAt = createdAt; ExpiresAt = expiresAt;
        this.refreshTokens = refreshTokens?.ToList() ?? throw new ArgumentNullException(nameof(refreshTokens));
        if (this.refreshTokens.Any(t => t.SessionId != id)) throw new ArgumentException("Refresh não pertence à sessão.", nameof(refreshTokens));
        if (this.refreshTokens.Select(t => t.Id).Distinct().Count() != this.refreshTokens.Count ||
            this.refreshTokens.Select(t => t.Hash).Distinct(StringComparer.Ordinal).Count() != this.refreshTokens.Count)
            throw new ArgumentException("Refresh IDs e hashes devem ser únicos.", nameof(refreshTokens));
        if (revokedAt is null && (rehydrating
            ? this.refreshTokens.Count(t => t.RevokedAt is null) != 1
            : this.refreshTokens.Count(t => t.IsActive(createdAt)) != 1))
            throw new ArgumentException("Sessão deve possuir exatamente um refresh ativo.", nameof(refreshTokens));
        if (revokedAt is not null && this.refreshTokens.Any(t => t.RevokedAt is null))
            throw new ArgumentException("Sessão revogada não pode possuir refresh ativo.", nameof(refreshTokens));
        RevokedAt = revokedAt;
        RevocationReason = revocationReason;
        Version = version;
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public SessionRevocationReason? RevocationReason { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<RefreshToken> RefreshTokens => refreshTokens.AsReadOnly();
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;

    public DomainResult Rotate(string presentedHash, RefreshToken replacement, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(presentedHash) || replacement is null) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (IsRevoked) return DomainResult.Failure(DomainErrorCode.SessionRevoked);
        if (IsExpired(now)) return DomainResult.Failure(DomainErrorCode.SessionExpired);
        if (replacement.SessionId != Id) return DomainResult.Failure(DomainErrorCode.RefreshTokenNotInSession);

        var known = refreshTokens.FirstOrDefault(t => t.Hash == presentedHash);
        if (known is null) return DomainResult.Failure(DomainErrorCode.RefreshTokenNotInSession);
        if (known.RevokedAt is not null)
        {
            Revoke(now, SessionRevocationReason.RefreshTokenReuse);
            return DomainResult.Failure(DomainErrorCode.RefreshTokenReuse);
        }
        if (known.IsExpired(now)) return DomainResult.Failure(DomainErrorCode.RefreshTokenExpired);
        if (!replacement.IsActive(now)) return DomainResult.Failure(replacement.IsExpired(now) ? DomainErrorCode.RefreshTokenExpired : DomainErrorCode.InvalidRefreshToken);
        if (refreshTokens.Any(t => t.Id == replacement.Id || t.Hash == replacement.Hash)) return DomainResult.Failure(DomainErrorCode.ReplacementAlreadyKnown);

        known.Revoke(now);
        refreshTokens.Add(replacement);
        Version++;
        return DomainResult.Success();
    }

    public DomainResult Revoke(DateTimeOffset now, SessionRevocationReason reason)
    {
        if (now < CreatedAt) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (refreshTokens.Any(token => now < token.CreatedAt)) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        var changed = RevokedAt is null;
        RevokedAt ??= now;
        RevocationReason ??= reason;
        foreach (var token in refreshTokens) token.Revoke(now);
        if (changed) Version++;
        return DomainResult.Success();
    }
}
