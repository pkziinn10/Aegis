using Aegis.Domain.Entities;
using Aegis.Domain.Enums;

namespace Aegis.Domain.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByRefreshTokenHashWithHistoryAsync(string refreshTokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(Session session, CancellationToken cancellationToken = default);
    /// <summary>
    /// Cria sessão somente se usuário associado existir, estiver ativo e mantiver
    /// <paramref name="expectedUserVersion"/>. Verificação e inserção são atômicas.
    /// </summary>
    Task<SessionCreationResult> AddIfUserActiveAtomicallyAsync(Session session, long expectedUserVersion,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Carrega histórico, detecta reuso e, nessa mesma transação, revoga a família
    /// antes de retornar <see cref="SessionRotationCode.RefreshTokenReuse"/>.
    /// Reuso de hash revogado prevalece sobre conflito CAS: a implementação deve
    /// recarregar o histórico e revogar a família atomicamente antes de retornar
    /// <see cref="SessionRotationCode.RefreshTokenReuse"/>.
    /// </summary>
    Task<SessionRotationResult> RotateAndPersistAtomicallyAsync(Guid sessionId, string presentedHash,
        RefreshToken replacement, DateTimeOffset now, long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<SessionOperationResult> RevokeAndPersistAtomicallyAsync(Guid sessionId, DateTimeOffset now,
        SessionRevocationReason reason, long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<SessionOperationResult> RevokeAllByUserIdAtomicallyAsync(Guid userId, DateTimeOffset now,
        SessionRevocationReason reason, CancellationToken cancellationToken = default);
    /// <summary>
    /// Revoga sessão identificada pelo hash apresentado, incluindo histórico.
    /// Ausência, expiração e revogação prévia convergem para sucesso idempotente.
    /// </summary>
    Task<SessionOperationResult> RevokeByRefreshTokenHashAtomicallyAsync(string presentedHash, DateTimeOffset now,
        SessionRevocationReason reason, CancellationToken cancellationToken = default);
}

public enum SessionCreationCode
{
    Succeeded = 0,
    UserNotFoundOrInactive,
    ConcurrencyConflict,
    DomainFailure
}

public sealed record SessionCreationResult(SessionCreationCode Code, Domain.Results.DomainResult? DomainResult = null)
{
    public bool IsSuccess => Code == SessionCreationCode.Succeeded;
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
