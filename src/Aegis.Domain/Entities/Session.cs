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
        var hasValidIdentity = id != Guid.Empty;
        if (!hasValidIdentity)
            throw new ArgumentException("Identidade inválida.", nameof(id));

        var hasValidUser = userId != Guid.Empty;
        if (!hasValidUser)
            throw new ArgumentException("Usuário inválido.", nameof(userId));

        var hasValidChronology = expiresAt > createdAt;
        if (!hasValidChronology)
            throw new ArgumentException("Sessão deve expirar depois da criação.", nameof(expiresAt));

        var hasValidVersion = version >= 1;
        if (!hasValidVersion)
            throw new ArgumentOutOfRangeException(nameof(version));

        var hasRevocationDate = revokedAt is not null;
        var hasRevocationReason = revocationReason is not null;
        var hasCoherentRevocation = hasRevocationDate == hasRevocationReason;
        if (!hasCoherentRevocation)
            throw new ArgumentException("Revogação exige data e motivo.");

        var hasValidRevocationReason = revocationReason is null || Enum.IsDefined(revocationReason.Value);
        if (!hasValidRevocationReason)
            throw new ArgumentOutOfRangeException(nameof(revocationReason));

        var hasValidRevocationDate = revokedAt is null || revokedAt >= createdAt;
        if (!hasValidRevocationDate)
            throw new ArgumentException("Data de revogação incoerente.", nameof(revokedAt));

        Id = id;
        UserId = userId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        this.refreshTokens = refreshTokens?.ToList() ?? throw new ArgumentNullException(nameof(refreshTokens));

        var allRefreshTokensBelongToSession = this.refreshTokens.All(t => t.SessionId == id);
        if (!allRefreshTokensBelongToSession)
            throw new ArgumentException("Refresh não pertence à sessão.", nameof(refreshTokens));

        var refreshTokensHaveValidChronology = this.refreshTokens.All(t =>
            t.CreatedAt >= createdAt && t.ExpiresAt <= expiresAt);
        if (!refreshTokensHaveValidChronology)
            throw new ArgumentException("Cronologia de refresh incoerente com a sessão.", nameof(refreshTokens));

        var refreshRevocationsFitSessionRevocation = revokedAt is null ||
            this.refreshTokens.All(t => t.RevokedAt <= revokedAt);
        if (!refreshRevocationsFitSessionRevocation)
            throw new ArgumentException("Revogação de refresh posterior à sessão.", nameof(refreshTokens));

        var haveUniqueRefreshIds = this.refreshTokens.Select(t => t.Id).Distinct().Count() == this.refreshTokens.Count;
        if (!haveUniqueRefreshIds)
            throw new ArgumentException("Refresh IDs e hashes devem ser únicos.", nameof(refreshTokens));

        var haveUniqueRefreshHashes = this.refreshTokens
            .Select(t => t.Hash)
            .Distinct(StringComparer.Ordinal)
            .Count() == this.refreshTokens.Count;
        if (!haveUniqueRefreshHashes)
            throw new ArgumentException("Refresh IDs e hashes devem ser únicos.", nameof(refreshTokens));

        var hasExactlyOneRefreshAvailable = rehydrating
            ? this.refreshTokens.Count(t => t.RevokedAt is null) == 1
            : this.refreshTokens.Count(t => t.IsActive(createdAt)) == 1;
        var requiresActiveRefresh = revokedAt is null;
        if (requiresActiveRefresh && !hasExactlyOneRefreshAvailable)
            throw new ArgumentException("Sessão deve possuir exatamente um refresh ativo.", nameof(refreshTokens));

        var revokedSessionHasNoActiveRefresh = this.refreshTokens.All(t => t.RevokedAt is not null);
        var isRevokedSession = revokedAt is not null;
        if (isRevokedSession && !revokedSessionHasNoActiveRefresh)
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
        if (string.IsNullOrWhiteSpace(presentedHash)) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (IsRevoked) return DomainResult.Failure(DomainErrorCode.SessionRevoked);
        if (IsExpired(now)) return DomainResult.Failure(DomainErrorCode.SessionExpired);

        var known = refreshTokens.FirstOrDefault(t => t.Hash == presentedHash);
        if (known is null) return DomainResult.Failure(DomainErrorCode.RefreshTokenNotInSession);
        if (known.RevokedAt is not null)
        {
            var revokeResult = Revoke(now, SessionRevocationReason.RefreshTokenReuse);
            if (revokeResult.IsFailure) return revokeResult;
            return DomainResult.Failure(DomainErrorCode.RefreshTokenReuse);
        }
        if (replacement is null) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (replacement.SessionId != Id) return DomainResult.Failure(DomainErrorCode.RefreshTokenNotInSession);
        if (replacement.CreatedAt < CreatedAt || replacement.ExpiresAt > ExpiresAt)
            return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (now < known.CreatedAt) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
        if (known.IsExpired(now)) return DomainResult.Failure(DomainErrorCode.RefreshTokenExpired);
        if (!replacement.IsActive(now)) return DomainResult.Failure(replacement.IsExpired(now) ? DomainErrorCode.RefreshTokenExpired : DomainErrorCode.InvalidRefreshToken);
        if (refreshTokens.Any(t => t.Id == replacement.Id || t.Hash == replacement.Hash)) return DomainResult.Failure(DomainErrorCode.ReplacementAlreadyKnown);

        var consumeResult = known.Revoke(now);
        if (consumeResult.IsFailure) return consumeResult;
        refreshTokens.Add(replacement);
        Version++;
        return DomainResult.Success();
    }

    public DomainResult Revoke(DateTimeOffset now, SessionRevocationReason reason)
    {
        if (!Enum.IsDefined(reason)) return DomainResult.Failure(DomainErrorCode.InvalidRefreshToken);
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
