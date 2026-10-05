using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class ChangePasswordUseCase(IUserRepository users, ISessionRepository sessions, ICurrentUserContext context, IPasswordHasher hasher, IClock clock, IUnitOfWork unit, IAuditWriter audit)
{
    public async Task<ApplicationResult> ExecuteAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        if (context.UserId is not Guid id) return ApplicationResult.Failure(ApplicationErrorCode.Unauthorized);
        var newPassword = Password.Create(command?.NewPassword);
        if (newPassword.IsFailure) return ApplicationResult.Failure(ApplicationErrorCode.WeakPassword);
        var user = await users.GetByIdAsync(id, ct); if (user is null) return ApplicationResult.Failure(ApplicationErrorCode.UserNotFound);
        if (user.CanChangePassword().IsFailure) return ApplicationResult.Failure(ApplicationErrorCode.InactiveUser);
        var currentPassword = Password.Create(command!.CurrentPassword);
        if (currentPassword.IsFailure || !hasher.Verify(currentPassword.Value!.Value, user.PasswordHash.Value)) return ApplicationResult.Failure(ApplicationErrorCode.InvalidCredentials);
        var expected = user.Version;
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var hash = PasswordHash.Create(hasher.Hash(newPassword.Value!.Value));
            if (hash.IsFailure) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(ApplicationErrorCode.InvalidPasswordHash), TransactionDecision.Rollback);
            var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(id, clock.UtcNow, SessionRevocationReason.PasswordChanged, transactionCt);
            if (!revoked.IsSuccess && revoked.Code != SessionOperationCode.NotFound) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(revoked)), TransactionDecision.Rollback);
            var passwordChange = user.ChangePasswordHash(hash.Value!);
            if (passwordChange.IsFailure) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(passwordChange.Code)), TransactionDecision.Rollback);
            var updated = await users.UpdateAtomicallyAsync(user, expected, transactionCt);
            if (!updated.IsSuccess) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthenticationResultMapper.Map(updated)), TransactionDecision.Rollback);
            await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.PasswordChange, id, SecurityAuditResult.Success), transactionCt);
            return new TransactionOutcome<ApplicationResult>(ApplicationResult.Success(), TransactionDecision.Commit);
        }, ct);
    }
}
