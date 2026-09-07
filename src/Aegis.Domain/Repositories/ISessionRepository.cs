using Aegis.Domain.Entities;
using Aegis.Domain.Enums;

namespace Aegis.Domain.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByRefreshTokenHashWithHistoryAsync(string refreshTokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(Session session, CancellationToken cancellationToken = default);
    /// <summary>
    /// Carrega histórico, detecta reuso e, nessa mesma transação, revoga a família
    /// antes de retornar <see cref="SessionRotationCode.RefreshTokenReuse"/>.
    /// </summary>
    Task<SessionRotationResult> RotateAndPersistAtomicallyAsync(Guid sessionId, string presentedHash,
        RefreshToken replacement, DateTimeOffset now, long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<SessionOperationResult> RevokeAndPersistAtomicallyAsync(Guid sessionId, DateTimeOffset now,
        SessionRevocationReason reason, long expectedVersion,
        CancellationToken cancellationToken = default);
}

public enum SessionRotationCode
{
    Succeeded = 0,
    NotFound,
    RefreshTokenReuse,
    ConcurrencyConflict,
    DomainFailure
}

public sealed record SessionRotationResult(SessionRotationCode Code, Domain.Results.DomainResult? DomainResult = null)
{
    public bool IsSuccess => Code == SessionRotationCode.Succeeded;
}

public enum SessionOperationCode
{
    Succeeded = 0,
    NotFound,
    ConcurrencyConflict,
    DomainFailure
}

public sealed record SessionOperationResult(SessionOperationCode Code, Domain.Results.DomainResult? DomainResult = null)
{
    public bool IsSuccess => Code == SessionOperationCode.Succeeded;
}
