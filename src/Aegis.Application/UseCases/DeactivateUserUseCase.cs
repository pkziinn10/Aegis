using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class DeactivateUserUseCase(IUserRepository users, ISessionRepository sessions, ICurrentUserContext context, IClock clock, IUnitOfWork unit, IAuditWriter audit)
{
    public async Task<ApplicationResult> ExecuteAsync(DeactivateUserCommand? command = null, CancellationToken ct = default)
    {
        if (context.UserId is not Guid id) return ApplicationResult.Failure(ApplicationErrorCode.Unauthorized);
        var user = await users.GetByIdAsync(id, ct);
        if (user is null) return ApplicationResult.Failure(ApplicationErrorCode.UserNotFound);
        var expected = user.Version;

        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(id, clock.UtcNow, SessionRevocationReason.UserDeactivated, transactionCt);
            if (!revoked.IsSuccess && revoked.Code != SessionOperationCode.NotFound)
                return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(revoked)), TransactionDecision.Rollback);

            var deactivation = user.Deactivate();
            if (deactivation.IsFailure)
                return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(deactivation.Code)), TransactionDecision.Rollback);

            var updated = await users.UpdateAtomicallyAsync(user, expected, transactionCt);
            if (!updated.IsSuccess)
                return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(updated)), TransactionDecision.Rollback);

            await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.UserDeactivation, id, SecurityAuditResult.Success), transactionCt);
            return new TransactionOutcome<ApplicationResult>(ApplicationResult.Success(), TransactionDecision.Commit);
        }, ct);
    }
}
