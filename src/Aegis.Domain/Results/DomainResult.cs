namespace Aegis.Domain.Results;

public enum DomainErrorCode
{
    None = 0,
    InvalidEmail,
    InvalidPasswordHash,
    InvalidRole,
    InvalidRefreshToken,
    RefreshTokenExpired,
    RefreshTokenRevoked,
    RefreshTokenReuse,
    SessionExpired,
    SessionRevoked,
    RefreshTokenNotInSession,
    ReplacementAlreadyKnown
}

public class DomainResult
{
    protected DomainResult(bool isSuccess, DomainErrorCode errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public DomainErrorCode ErrorCode { get; }
    public DomainErrorCode Code => ErrorCode;
    public string? ErrorMessage { get; }

    public static DomainResult Success() => new(true, DomainErrorCode.None, null);
    public static DomainResult Failure(DomainErrorCode code, string? message = null) => new(false, code, message);
}

public sealed class DomainResult<T> : DomainResult
{
    private DomainResult(bool isSuccess, T? value, DomainErrorCode code, string? message)
        : base(isSuccess, code, message) => Value = value;

    public T? Value { get; }

    public static DomainResult<T> Success(T value) => new(true, value, DomainErrorCode.None, null);
    public static new DomainResult<T> Failure(DomainErrorCode code, string? message = null) => new(false, default, code, message);
}
