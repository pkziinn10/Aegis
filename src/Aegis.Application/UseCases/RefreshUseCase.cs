using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class RefreshUseCase(ISessionRepository sessions, IRefreshTokenFactory factory, IAccessTokenIssuer issuer, IUserRepository users, IClock clock, IUnitOfWork unit, IRefreshTokenPolicy policy, IAuditWriter audit)
{
    public async Task<ApplicationResult<TokenResult>> ExecuteAsync(RefreshCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command?.RefreshToken)) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        var now = clock.UtcNow; var hash = factory.Hash(new RefreshTokenValue(command.RefreshToken)); var session = await sessions.GetByRefreshTokenHashWithHistoryAsync(hash, ct);
        if (session is null) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        if (session.IsExpired(now)) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.SessionExpired);
        if (session.IsRevoked) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.SessionRevoked);
        var currentUser = await users.GetByIdAsync(session.UserId, ct);
        if (currentUser is null || currentUser.CanAuthenticate().IsFailure)
        {
            return await unit.ExecuteInTransactionAsync(async transactionCt =>
            {
                var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(session.UserId, now, SessionRevocationReason.UserDeactivated, transactionCt);
                var success = revoked.IsSuccess || revoked.Code == SessionOperationCode.NotFound;
                return new TransactionOutcome<ApplicationResult<TokenResult>>(
                    ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials),
                    success ? TransactionDecision.Commit : TransactionDecision.Rollback);
            }, ct);
        }
        var expiry = policy.GetRotationExpiration(now, session.ExpiresAt); var replacement = factory.Create(session.Id, now, expiry); var domainToken = new RefreshToken(Guid.NewGuid(), session.Id, replacement.Hash, now, expiry);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var rotation = await sessions.RotateAndPersistAtomicallyAsync(session.Id, hash, domainToken, now, session.Version, transactionCt);
            if (!rotation.IsSuccess)
            {
                var decision = rotation.Code == SessionRotationCode.RefreshTokenReuse
                    ? TransactionDecision.Commit : TransactionDecision.Rollback;
                if (rotation.Code == SessionRotationCode.RefreshTokenReuse) await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.RefreshTokenReuse, session.UserId, SecurityAuditResult.FamilyRevoked, session.Id), transactionCt);
                return new TransactionOutcome<ApplicationResult<TokenResult>>(ApplicationResult<TokenResult>.Failure(AuthenticationResultMapper.Map(rotation)), decision);
            }

            var user = await users.GetByIdAsync(session.UserId, transactionCt);
            if (user is null || user.CanAuthenticate().IsFailure)
            {
                var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(session.UserId, now, SessionRevocationReason.UserDeactivated, transactionCt);
                var revokeOk = revoked.IsSuccess || revoked.Code == SessionOperationCode.NotFound;
                var revokeResult = revokeOk
                    ? ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials)
                    : ApplicationResult<TokenResult>.Failure(AuthenticationResultMapper.Map(revoked));
                return new TransactionOutcome<ApplicationResult<TokenResult>>(
                    revokeResult,
                    revokeOk ? TransactionDecision.Commit : TransactionDecision.Rollback);
            }

            var access = issuer.Issue(user.Id, user.Role, now);
            await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.RefreshRotation, user.Id, SecurityAuditResult.Success, session.Id), transactionCt);
            return new TransactionOutcome<ApplicationResult<TokenResult>>(
                ApplicationResult<TokenResult>.Success(new(access, new(replacement.Value.Value))), TransactionDecision.Commit);
        }, ct);
    }
}
