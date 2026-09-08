namespace Aegis.Application.Results;

public enum ApplicationErrorCode
{
    None = 0, InvalidRequest, WeakPassword, InvalidCredentials, EmailAlreadyRegistered,
    UserNotFound, InactiveUser, SessionNotFound, InvalidRefreshToken, RefreshTokenReuse,
    SessionExpired, SessionRevoked, ConcurrencyConflict, InvalidPasswordHash, Unauthorized
}

public class ApplicationResult
{
    protected ApplicationResult(bool success, ApplicationErrorCode code, string? message = null)
        => (IsSuccess, ErrorCode, ErrorMessage) = (success, code, message);
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ApplicationErrorCode ErrorCode { get; }
    public ApplicationErrorCode Code => ErrorCode;
    public string? ErrorMessage { get; }
    public static ApplicationResult Success() => new(true, ApplicationErrorCode.None);
    public static ApplicationResult Failure(ApplicationErrorCode code, string? message = null) => new(false, code, message);
}

public sealed class ApplicationResult<T> : ApplicationResult
{
    private ApplicationResult(bool success, T? value, ApplicationErrorCode code, string? message)
        : base(success, code, message) => Value = value;
    public T? Value { get; }
    public static ApplicationResult<T> Success(T value) => new(true, value, ApplicationErrorCode.None, null);
    public static new ApplicationResult<T> Failure(ApplicationErrorCode code, string? message = null)
        => new(false, default, code, message);
}
