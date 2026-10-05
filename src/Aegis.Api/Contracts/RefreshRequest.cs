namespace Aegis.Api.Controllers;

public sealed record RefreshRequest(string RefreshToken)
{
    public override string ToString() => nameof(RefreshRequest);
}
