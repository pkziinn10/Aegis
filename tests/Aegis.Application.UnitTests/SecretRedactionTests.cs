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
        var material = new RefreshTokenMaterial(refresh, "hash-secret", DateTimeOffset.UtcNow, refresh.ExpiresAt!.Value);

        Assert.DoesNotContain("access-secret", access.ToString());
        Assert.DoesNotContain("refresh-secret", refresh.ToString());
        Assert.DoesNotContain("hash-secret", material.ToString());
        Assert.DoesNotContain("password-secret", new ChangePasswordCommand("password-secret", "new-password-secret").ToString());
        Assert.DoesNotContain("refresh-secret", new RefreshCommand(refresh.Value).ToString());

        var tokens = new TokenResult(access, new RefreshTokenDto(refresh.Value));
        Assert.DoesNotContain("access-secret", tokens.ToString());
        Assert.DoesNotContain("refresh-secret", tokens.ToString());
        Assert.DoesNotContain("access-secret", new LoginResult(new(Guid.NewGuid(), "safe@a.com", Aegis.Domain.Enums.UserRole.User), tokens).ToString());
        Assert.DoesNotContain("refresh-secret", new RegisterResult(new(Guid.NewGuid(), "safe@a.com", Aegis.Domain.Enums.UserRole.User), tokens).ToString());
    }
}
