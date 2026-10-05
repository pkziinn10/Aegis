using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class RegisterUseCase(IUserRepository users, ISessionRepository sessions, IPasswordHasher hasher, IAccessTokenIssuer issuer, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit, IRefreshTokenPolicy policy)
{
    public async Task<ApplicationResult<RegisterResult>> ExecuteAsync(RegisterCommand command, CancellationToken ct = default)
    {
        var password = Password.Create(command?.Password);
        if (password.IsFailure) return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.WeakPassword);
        var email = AuthRules.ParseEmail(command!.Email); if (email is null) return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.InvalidRequest);
        var hash = PasswordHash.Create(hasher.Hash(password.Value!.Value));
        if (hash.IsFailure) return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.InvalidPasswordHash);
        var user = new User(Guid.NewGuid(), email, hash.Value!, UserRole.User);
        var now = clock.UtcNow; var expiry = policy.GetSessionExpiration(now); var sessionId = Guid.NewGuid(); var material = factory.Create(sessionId, now, expiry);
        var access = issuer.Issue(user.Id, user.Role, now);
        try
        {
            var existing = await users.GetByEmailAsync(email, ct);
            if (existing is not null)
                return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.EmailAlreadyRegistered);

            return await unit.ExecuteInTransactionAsync(async transactionCt =>
            {
                await users.AddAsync(user, transactionCt);
                await sessions.AddAsync(new Session(sessionId, user.Id, now, expiry, [new RefreshToken(Guid.NewGuid(), sessionId, material.Hash, now, expiry)]), transactionCt);
                return new TransactionOutcome<ApplicationResult<RegisterResult>>(
                    ApplicationResult<RegisterResult>.Success(new(AuthRules.Dto(user), new(access, new(material.Value.Value)))), TransactionDecision.Commit);
            }, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // The unit of work has already rolled back the aborted transaction.
            // Only a confirmed row turns this technical race into a business result.
            if (await users.GetByEmailAsync(email, ct) is not null)
                return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.EmailAlreadyRegistered);
            throw;
        }
    }
}
