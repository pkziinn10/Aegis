using Aegis.Application.Abstractions;
using Aegis.Domain.Enums;

namespace Aegis.Application.Contracts;

public sealed record RegisterCommand(string Email, string Password)
{
    public override string ToString() => $"RegisterCommand(Email={Email})";
}
public sealed record LoginCommand(string Email, string Password)
{
    public override string ToString() => $"LoginCommand(Email={Email})";
}
public sealed record RefreshCommand(string? RefreshToken)
{
    public override string ToString() => "RefreshCommand(RefreshToken=[REDACTED])";
}
public sealed record LogoutCommand(string? RefreshToken)
{
    public override string ToString() => "LogoutCommand(RefreshToken=[REDACTED])";
}
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
{
    public override string ToString() => "ChangePasswordCommand(CurrentPassword=[REDACTED], NewPassword=[REDACTED])";
}
public sealed record UserDto(Guid Id, string Email, UserRole Role);
public sealed record RefreshTokenDto(string Value)
{
    public override string ToString() => "RefreshTokenDto(Value=[REDACTED])";
}
public sealed record TokenResult(AccessToken AccessToken, RefreshTokenDto RefreshToken)
{
    public override string ToString() => "TokenResult(AccessToken=[REDACTED], RefreshToken=[REDACTED])";
}
public sealed record RegisterResult(UserDto User, TokenResult Tokens)
{
    public override string ToString() => $"RegisterResult(User={User}, Tokens=[REDACTED])";
}
public sealed record LoginResult(UserDto User, TokenResult Tokens)
{
    public override string ToString() => $"LoginResult(User={User}, Tokens=[REDACTED])";
}
