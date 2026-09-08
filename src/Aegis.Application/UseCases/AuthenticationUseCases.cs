using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

internal static class AuthRules
{
    public static bool ValidPassword(string? value) => !string.IsNullOrEmpty(value) && value.Length >= 12;
    public static Email? ParseEmail(string? value) => value is null ? null : Email.Create(value).Value;
    public static UserDto Dto(User user) => new(user.Id, user.Email.Value, user.Role);
    public static DateTimeOffset RefreshExpiry(DateTimeOffset now, DateTimeOffset sessionExpiry) => now.AddDays(7) < sessionExpiry ? now.AddDays(7) : sessionExpiry;
    public static ApplicationErrorCode Map(SessionOperationResult result) => result.Code switch
    {
        SessionOperationCode.NotFound => ApplicationErrorCode.SessionNotFound,
        SessionOperationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionOperationCode.DomainFailure => Map(result.DomainResult?.ErrorCode ?? Aegis.Domain.Results.DomainErrorCode.InvalidRefreshToken),
        _ => ApplicationErrorCode.InvalidRequest
    };
    public static ApplicationErrorCode Map(SessionCreationResult result) => result.Code switch
    {
        SessionCreationCode.UserNotFoundOrInactive => ApplicationErrorCode.InvalidCredentials,
        SessionCreationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionCreationCode.DomainFailure => Map(result.DomainResult?.ErrorCode ?? Aegis.Domain.Results.DomainErrorCode.InvalidRefreshToken),
        SessionCreationCode.Succeeded => ApplicationErrorCode.None,
        _ => ApplicationErrorCode.InvalidRequest
    };
    public static ApplicationErrorCode Map(SessionRotationResult result) => result.Code switch
    {
        SessionRotationCode.NotFound => ApplicationErrorCode.InvalidCredentials,
        SessionRotationCode.RefreshTokenReuse => ApplicationErrorCode.RefreshTokenReuse,
        SessionRotationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionRotationCode.DomainFailure => Map(result.DomainResult?.ErrorCode ?? Aegis.Domain.Results.DomainErrorCode.InvalidRefreshToken),
        SessionRotationCode.Succeeded => ApplicationErrorCode.None,
        _ => ApplicationErrorCode.InvalidRefreshToken
    };
    public static ApplicationErrorCode Map(Aegis.Domain.Results.DomainErrorCode code) => code switch
    {
        Aegis.Domain.Results.DomainErrorCode.None => ApplicationErrorCode.None,
        Aegis.Domain.Results.DomainErrorCode.InvalidEmail => ApplicationErrorCode.InvalidRequest,
        Aegis.Domain.Results.DomainErrorCode.InvalidRole => ApplicationErrorCode.InvalidRequest,
        Aegis.Domain.Results.DomainErrorCode.InvalidPasswordHash => ApplicationErrorCode.InvalidPasswordHash,
        Aegis.Domain.Results.DomainErrorCode.InvalidRefreshToken => ApplicationErrorCode.InvalidRefreshToken,
        Aegis.Domain.Results.DomainErrorCode.RefreshTokenExpired => ApplicationErrorCode.SessionExpired,
        Aegis.Domain.Results.DomainErrorCode.RefreshTokenRevoked => ApplicationErrorCode.SessionRevoked,
        Aegis.Domain.Results.DomainErrorCode.RefreshTokenReuse => ApplicationErrorCode.RefreshTokenReuse,
        Aegis.Domain.Results.DomainErrorCode.SessionExpired => ApplicationErrorCode.SessionExpired,
        Aegis.Domain.Results.DomainErrorCode.SessionRevoked => ApplicationErrorCode.SessionRevoked,
        Aegis.Domain.Results.DomainErrorCode.RefreshTokenNotInSession => ApplicationErrorCode.InvalidRefreshToken,
        Aegis.Domain.Results.DomainErrorCode.ReplacementAlreadyKnown => ApplicationErrorCode.InvalidRefreshToken,
        _ => ApplicationErrorCode.InvalidRequest
    };
}

public sealed class RegisterUseCase(IUserRepository users, ISessionRepository sessions, IPasswordHasher hasher, IAccessTokenIssuer issuer, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit)
{
    public async Task<ApplicationResult<RegisterResult>> ExecuteAsync(RegisterCommand command, CancellationToken ct = default)
    {
        if (command is null || !AuthRules.ValidPassword(command.Password)) return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.WeakPassword);
        var email = AuthRules.ParseEmail(command.Email); if (email is null) return ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.InvalidRequest);
        var user = new User(Guid.NewGuid(), email, hasher.Hash(command.Password), UserRole.User);
        var now = clock.UtcNow; var expiry = now.AddDays(7); var sessionId = Guid.NewGuid(); var material = factory.Create(sessionId, now, expiry);
        var access = issuer.Issue(user.Id, user.Role, now);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var inserted = await users.AddIfNotExistsAtomicallyAsync(user, transactionCt);
            if (!inserted.IsSuccess)
                return new TransactionOutcome<ApplicationResult<RegisterResult>>(
                    ApplicationResult<RegisterResult>.Failure(ApplicationErrorCode.EmailAlreadyRegistered), TransactionDecision.Rollback);
            await sessions.AddAsync(new Session(sessionId, user.Id, now, expiry, [new RefreshToken(Guid.NewGuid(), sessionId, material.Hash, now, expiry)]), transactionCt);
            return new TransactionOutcome<ApplicationResult<RegisterResult>>(
                ApplicationResult<RegisterResult>.Success(new(AuthRules.Dto(user), new(access, material.Value))), TransactionDecision.Commit);
        }, ct);
    }
}

public sealed class LoginUseCase(IUserRepository users, ISessionRepository sessions, IPasswordHasher hasher, IAccessTokenIssuer issuer, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit, IAuditWriter? audit = null)
{
    public async Task<ApplicationResult<LoginResult>> ExecuteAsync(LoginCommand command, CancellationToken ct = default)
    {
        var email = AuthRules.ParseEmail(command?.Email); var user = email is null ? null : await users.GetByEmailAsync(email, ct);
        var hash = user?.PasswordHash ?? hasher.DummyHash;
        var valid = hasher.Verify(command?.Password ?? string.Empty, hash);
        if (user is null || !user.IsActive || !valid) return ApplicationResult<LoginResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        var now = clock.UtcNow; var expiry = now.AddDays(7); var sessionId = Guid.NewGuid(); var material = factory.Create(sessionId, now, expiry);
        var access = issuer.Issue(user.Id, user.Role, now);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var creation = await sessions.AddIfUserActiveAtomicallyAsync(
                new Session(sessionId, user.Id, now, expiry, [new RefreshToken(Guid.NewGuid(), sessionId, material.Hash, now, expiry)]),
                user.Version, transactionCt);
            if (!creation.IsSuccess)
                return new TransactionOutcome<ApplicationResult<LoginResult>>(
                    ApplicationResult<LoginResult>.Failure(AuthRules.Map(creation)), TransactionDecision.Rollback);
            if (audit is not null) await audit.WriteAsync("login", user.Id, new Dictionary<string, string?> { ["result"] = "success" }, transactionCt);
            return new TransactionOutcome<ApplicationResult<LoginResult>>(
                ApplicationResult<LoginResult>.Success(new(AuthRules.Dto(user), new(access, material.Value))), TransactionDecision.Commit);
        }, ct);
    }
}

public sealed class RefreshUseCase(ISessionRepository sessions, IRefreshTokenFactory factory, IAccessTokenIssuer issuer, IUserRepository users, IClock clock, IUnitOfWork unit, IAuditWriter? audit = null)
{
    public async Task<ApplicationResult<TokenResult>> ExecuteAsync(RefreshCommand command, CancellationToken ct = default)
    {
        if (command?.RefreshToken is null) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        var now = clock.UtcNow; var hash = factory.Hash(command.RefreshToken); var session = await sessions.GetByRefreshTokenHashWithHistoryAsync(hash, ct);
        if (session is null) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials);
        if (session.IsExpired(now)) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.SessionExpired);
        if (session.IsRevoked) return ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.SessionRevoked);
        var expiry = AuthRules.RefreshExpiry(now, session.ExpiresAt); var replacement = factory.Create(session.Id, now, expiry); var domainToken = new RefreshToken(Guid.NewGuid(), session.Id, replacement.Hash, now, expiry);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var rotation = await sessions.RotateAndPersistAtomicallyAsync(session.Id, hash, domainToken, now, session.Version, transactionCt);
            if (!rotation.IsSuccess)
            {
                var decision = rotation.Code == SessionRotationCode.RefreshTokenReuse
                    ? TransactionDecision.Commit : TransactionDecision.Rollback;
                if (rotation.Code == SessionRotationCode.RefreshTokenReuse && audit is not null) await audit.WriteAsync("refresh_token_reuse", session.UserId, new Dictionary<string, string?> { ["result"] = "family_revoked" }, transactionCt);
                return new TransactionOutcome<ApplicationResult<TokenResult>>(ApplicationResult<TokenResult>.Failure(AuthRules.Map(rotation)), decision);
            }

            var user = await users.GetByIdAsync(session.UserId, transactionCt);
            if (user is null || !user.IsActive)
            {
                var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(session.UserId, now, SessionRevocationReason.Manual, transactionCt);
                var revokeOk = revoked.IsSuccess || revoked.Code == SessionOperationCode.NotFound;
                var revokeResult = revokeOk
                    ? ApplicationResult<TokenResult>.Failure(ApplicationErrorCode.InvalidCredentials)
                    : ApplicationResult<TokenResult>.Failure(AuthRules.Map(revoked));
                return new TransactionOutcome<ApplicationResult<TokenResult>>(
                    revokeResult,
                    revokeOk ? TransactionDecision.Commit : TransactionDecision.Rollback);
            }

            var access = issuer.Issue(user.Id, user.Role, now);
            if (audit is not null) await audit.WriteAsync("refresh_rotation", user.Id, new Dictionary<string, string?> { ["result"] = "success", ["sessionId"] = session.Id.ToString("N") }, transactionCt);
            return new TransactionOutcome<ApplicationResult<TokenResult>>(
                ApplicationResult<TokenResult>.Success(new(access, replacement.Value)), TransactionDecision.Commit);
        }, ct);
    }
}

public sealed class LogoutUseCase(ISessionRepository sessions, IRefreshTokenFactory factory, IClock clock, IUnitOfWork unit, ICurrentUserContext? context = null, IAuditWriter? audit = null)
{
    public async Task<ApplicationResult> ExecuteAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (command?.RefreshToken is null) return ApplicationResult.Success();
        var now = clock.UtcNow;
        var result = await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var operation = await sessions.RevokeByRefreshTokenHashAtomicallyAsync(
                factory.Hash(command.RefreshToken), now, SessionRevocationReason.Manual, transactionCt);
            var success = operation.IsSuccess || operation.Code == SessionOperationCode.NotFound;
            if (success && audit is not null) await audit.WriteAsync("logout", context?.UserId, new Dictionary<string, string?> { ["result"] = "success" }, transactionCt);
            return new TransactionOutcome<ApplicationResult>(
                success ? ApplicationResult.Success() : ApplicationResult.Failure(AuthRules.Map(operation)),
                success ? TransactionDecision.Commit : TransactionDecision.Rollback);
        }, ct);
        return result;
    }
}

public sealed class GetMeUseCase(IUserRepository users, ICurrentUserContext context)
{
    public async Task<ApplicationResult<UserDto>> ExecuteAsync(CancellationToken ct = default)
    {
        if (context.UserId is not Guid id) return ApplicationResult<UserDto>.Failure(ApplicationErrorCode.Unauthorized);
        var user = await users.GetByIdAsync(id, ct); if (user is null) return ApplicationResult<UserDto>.Failure(ApplicationErrorCode.UserNotFound);
        return !user.IsActive ? ApplicationResult<UserDto>.Failure(ApplicationErrorCode.InactiveUser) : ApplicationResult<UserDto>.Success(AuthRules.Dto(user));
    }
}

public sealed class ChangePasswordUseCase(IUserRepository users, ISessionRepository sessions, ICurrentUserContext context, IPasswordHasher hasher, IClock clock, IUnitOfWork unit, IAuditWriter? audit = null)
{
    public async Task<ApplicationResult> ExecuteAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        if (context.UserId is not Guid id) return ApplicationResult.Failure(ApplicationErrorCode.Unauthorized);
        if (command is null || !AuthRules.ValidPassword(command.NewPassword)) return ApplicationResult.Failure(ApplicationErrorCode.WeakPassword);
        var user = await users.GetByIdAsync(id, ct); if (user is null) return ApplicationResult.Failure(ApplicationErrorCode.UserNotFound);
        if (!user.IsActive) return ApplicationResult.Failure(ApplicationErrorCode.InactiveUser);
        if (string.IsNullOrEmpty(command.CurrentPassword)) return ApplicationResult.Failure(ApplicationErrorCode.InvalidCredentials);
        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash)) return ApplicationResult.Failure(ApplicationErrorCode.InvalidCredentials);
        var expected = user.Version;
        var updatedUser = User.Rehydrate(user.Id, user.Email, user.PasswordHash, user.Role, user.IsActive, user.Version);
        return await unit.ExecuteInTransactionAsync(async transactionCt =>
        {
            var passwordChange = updatedUser.ChangePasswordHash(hasher.Hash(command.NewPassword));
            if (passwordChange.IsFailure) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(passwordChange.ErrorCode == Aegis.Domain.Results.DomainErrorCode.InvalidPasswordHash ? ApplicationErrorCode.InvalidPasswordHash : ApplicationErrorCode.InvalidRequest), TransactionDecision.Rollback);
            var revoked = await sessions.RevokeAllByUserIdAtomicallyAsync(id, clock.UtcNow, SessionRevocationReason.PasswordChanged, transactionCt);
            if (!revoked.IsSuccess && revoked.Code != SessionOperationCode.NotFound) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(AuthRules.Map(revoked)), TransactionDecision.Rollback);
            var updated = await users.UpdateAtomicallyAsync(updatedUser, expected, transactionCt);
            if (!updated.IsSuccess) return new TransactionOutcome<ApplicationResult>(ApplicationResult.Failure(updated.Code switch
            {
                UserUpdateCode.NotFound => ApplicationErrorCode.UserNotFound,
                UserUpdateCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
                UserUpdateCode.Succeeded => ApplicationErrorCode.None,
                _ => ApplicationErrorCode.InvalidRequest
            }), TransactionDecision.Rollback);
            if (audit is not null) await audit.WriteAsync("password_change", id, new Dictionary<string, string?> { ["result"] = "success" }, transactionCt);
            return new TransactionOutcome<ApplicationResult>(ApplicationResult.Success(), TransactionDecision.Commit);
        }, ct);
    }
}
