using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;

namespace Aegis.Application.UnitTests;

public sealed class SecretRedactionTests
{
    [Fact]
    public void Public_representations_redact_credentials_and_tokens()
    {
        var access = new AccessToken("access-secret", "Bearer", DateTimeOffset.UtcNow.AddMinutes(5));
        var refresh = new RefreshTokenValue("refresh-secret", DateTimeOffset.UtcNow.AddDays(1));
        var material = new RefreshTokenMaterial(refresh, "hash-secret", DateTimeOffset.UtcNow, refresh.ExpiresAt);

        Assert.DoesNotContain("access-secret", access.ToString());
        Assert.DoesNotContain("refresh-secret", refresh.ToString());
        Assert.DoesNotContain("hash-secret", material.ToString());
        Assert.DoesNotContain("password-secret", new ChangePasswordCommand("password-secret", "new-password-secret").ToString());
        Assert.DoesNotContain("refresh-secret", new RefreshCommand(refresh).ToString());
    }
}
