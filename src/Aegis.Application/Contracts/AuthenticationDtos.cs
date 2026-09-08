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
public sealed record RefreshCommand(RefreshTokenValue RefreshToken)
{
    public override string ToString() => "RefreshCommand(RefreshToken=[REDACTED])";
}
public sealed record LogoutCommand(RefreshTokenValue RefreshToken)
{
    public override string ToString() => "LogoutCommand(RefreshToken=[REDACTED])";
}
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
{
    public override string ToString() => "ChangePasswordCommand(CurrentPassword=[REDACTED], NewPassword=[REDACTED])";
}
public sealed record UserDto(Guid Id, string Email, UserRole Role);
public sealed record TokenResult(AccessToken AccessToken, RefreshTokenValue RefreshToken);
public sealed record RegisterResult(UserDto User, TokenResult Tokens);
public sealed record LoginResult(UserDto User, TokenResult Tokens);
