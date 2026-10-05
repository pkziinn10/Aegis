namespace Aegis.Api.Controllers;

public sealed record TokenSessionResponse(Guid UserId, string Email, string Role, string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string RefreshToken)
{
    public override string ToString() => nameof(TokenSessionResponse);
}
