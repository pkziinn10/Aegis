namespace Aegis.Api.Controllers;

public sealed record CredentialsRequest(string Email, string Password)
{
    public override string ToString() => nameof(CredentialsRequest);
}
