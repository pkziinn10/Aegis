namespace Aegis.Api.Controllers;

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string RefreshToken)
{
    public override string ToString() => nameof(TokenResponse);
}
