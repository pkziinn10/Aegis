using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class LogoutUseCase(ISessionRepository sessions, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit, ICurrentUserContext context, IAuditWriter audit)
{
    public async Task<ApplicationResult> ExecuteAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command?.RefreshToken)) return ApplicationResult.Success();
        var now = clock.UtcNow;
        var result = await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var operation = await sessions.RevokeByRefreshTokenHashAtomicallyAsync(
                factory.Hash(new RefreshTokenValue(command.RefreshToken)), now, SessionRevocationReason.Manual, transactionCt);
            var success = operation.IsSuccess || operation.Code == SessionOperationCode.NotFound;
            if (success) await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.Logout, context?.UserId, SecurityAuditResult.Success), transactionCt);
            return new TransactionOutcome<ApplicationResult>(
                success ? ApplicationResult.Success() : ApplicationResult.Failure(AuthenticationResultMapper.Map(operation)),
                success ? TransactionDecision.Commit : TransactionDecision.Rollback);
        }, ct);
        return result;
    }
}
