namespace Aegis.Api.Controllers;

public sealed record BrowserSessionResponse(Guid UserId, string Email, string Role, string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string CsrfToken)
{
    public override string ToString() => nameof(BrowserSessionResponse);
}
