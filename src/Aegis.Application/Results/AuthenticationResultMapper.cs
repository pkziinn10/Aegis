using Aegis.Domain.Repositories;
using Aegis.Domain.Results;

namespace Aegis.Application.Results;

public static class AuthenticationResultMapper
{
    public static ApplicationErrorCode Map(DomainErrorCode code) => code switch
    {
        DomainErrorCode.None => ApplicationErrorCode.None,
        DomainErrorCode.InvalidEmail or DomainErrorCode.InvalidRole => ApplicationErrorCode.InvalidRequest,
        DomainErrorCode.InvalidPasswordHash => ApplicationErrorCode.InvalidPasswordHash,
        DomainErrorCode.InvalidRefreshToken or DomainErrorCode.RefreshTokenNotInSession or DomainErrorCode.ReplacementAlreadyKnown => ApplicationErrorCode.InvalidRefreshToken,
        DomainErrorCode.RefreshTokenExpired or DomainErrorCode.SessionExpired => ApplicationErrorCode.SessionExpired,
        DomainErrorCode.RefreshTokenRevoked or DomainErrorCode.SessionRevoked => ApplicationErrorCode.SessionRevoked,
        DomainErrorCode.RefreshTokenReuse => ApplicationErrorCode.RefreshTokenReuse,
        DomainErrorCode.AccountInactive or DomainErrorCode.UserAlreadyDeactivated => ApplicationErrorCode.InactiveUser,
        DomainErrorCode.InvalidPassword => ApplicationErrorCode.InvalidCredentials,
        _ => ApplicationErrorCode.InternalServerError
    };

    public static ApplicationErrorCode Map(SessionCreationResult result) => result.Code switch
    {
        SessionCreationCode.Succeeded => ApplicationErrorCode.None,
        SessionCreationCode.UserNotFoundOrInactive => ApplicationErrorCode.InvalidCredentials,
        SessionCreationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionCreationCode.DomainFailure => result.DomainResult is null ? ApplicationErrorCode.InternalServerError : Map(result.DomainResult.Code),
        _ => ApplicationErrorCode.InternalServerError
    };

    public static ApplicationErrorCode Map(SessionRotationResult result) => result.Code switch
    {
        SessionRotationCode.Succeeded => ApplicationErrorCode.None,
        SessionRotationCode.NotFound => ApplicationErrorCode.InvalidCredentials,
        SessionRotationCode.RefreshTokenReuse => ApplicationErrorCode.RefreshTokenReuse,
        SessionRotationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionRotationCode.DomainFailure => result.DomainResult is null ? ApplicationErrorCode.InternalServerError : Map(result.DomainResult.Code),
        _ => ApplicationErrorCode.InternalServerError
    };

    public static ApplicationErrorCode Map(SessionOperationResult result) => result.Code switch
    {
        SessionOperationCode.Succeeded => ApplicationErrorCode.None,
        SessionOperationCode.NotFound => ApplicationErrorCode.SessionNotFound,
        SessionOperationCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        SessionOperationCode.DomainFailure => result.DomainResult is null ? ApplicationErrorCode.InternalServerError : Map(result.DomainResult.Code),
        _ => ApplicationErrorCode.InternalServerError
    };

    public static ApplicationErrorCode Map(UserUpdateResult result) => result.Code switch
    {
        UserUpdateCode.Succeeded => ApplicationErrorCode.None,
        UserUpdateCode.NotFound => ApplicationErrorCode.UserNotFound,
        UserUpdateCode.ConcurrencyConflict => ApplicationErrorCode.ConcurrencyConflict,
        _ => ApplicationErrorCode.InternalServerError
    };
}
