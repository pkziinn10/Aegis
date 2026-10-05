using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class LoginUseCase(IUserRepository users, ISessionRepository sessions, IPasswordHasher hasher, IAccessTokenIssuer issuer, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit, IRefreshTokenPolicy policy, IAuditWriter audit)
{
    public async Task<ApplicationResult<LoginResult>> ExecuteAsync(LoginCommand command, CancellationToken ct = default)
    {
        var email = AuthRules.ParseEmail(command?.Email); var user = email is null ? null : await users.GetByEmailAsync(email, ct);
        var password = Password.Create(command?.Password);
        var hash = user?.PasswordHash.Value ?? hasher.DummyHash;
        var valid = password.IsSuccess && hasher.Verify(password.Value!.Value, hash);
        if (user is null || user.CanAuthenticate().IsFailure || !valid) return ApplicationResult<LoginResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        var now = clock.UtcNow; var expiry = policy.GetSessionExpiration(now); var sessionId = Guid.NewGuid(); var material = factory.Create(sessionId, now, expiry);
        var access = issuer.Issue(user.Id, user.Role, now);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var creation = await sessions.AddIfUserActiveAtomicallyAsync(
                new Session(sessionId, user.Id, now, expiry, [new RefreshToken(Guid.NewGuid(), sessionId, material.Hash, now, expiry)]),
                user.Version, transactionCt);
            if (!creation.IsSuccess)
                return new TransactionOutcome<ApplicationResult<LoginResult>>(
                    ApplicationResult<LoginResult>.Failure(AuthenticationResultMapper.Map(creation)), TransactionDecision.Rollback);
            await audit.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.Login, user.Id, SecurityAuditResult.Success), transactionCt);
            return new TransactionOutcome<ApplicationResult<LoginResult>>(
                ApplicationResult<LoginResult>.Success(new(AuthRules.Dto(user), new(access, new(material.Value.Value)))), TransactionDecision.Commit);
        }, ct);
    }
}
