using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Domain.Enums;
using System.Text.Json;

namespace Aegis.Application.UnitTests;

public sealed class SecretRedactionTests
{
    [Fact]
    public void Public_representations_redact_credentials_and_tokens()
    {
        var issuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var access = new AccessToken("eyJhbGciOiJIUzI1NiJ9.jwt-secret.signature", "Bearer", issuedAt.AddMinutes(5));
        var refresh = new RefreshTokenValue("refresh-secret", issuedAt.AddDays(1));
        var material = new RefreshTokenMaterial(refresh, "hash-secret", issuedAt, refresh.ExpiresAt!.Value);

        Assert.DoesNotContain(access.Value, access.ToString());
        Assert.DoesNotContain("refresh-secret", refresh.ToString());
        Assert.DoesNotContain("hash-secret", material.ToString());
        Assert.DoesNotContain("password-secret", new ChangePasswordCommand("password-secret", "new-password-secret").ToString());
        Assert.DoesNotContain("refresh-secret", new RefreshCommand(refresh.Value).ToString());
        Assert.DoesNotContain("refresh-secret", new LogoutCommand(refresh.Value).ToString());

        var tokens = new TokenResult(access, new RefreshTokenDto(refresh.Value));
        Assert.DoesNotContain(access.Value, tokens.ToString());
        Assert.DoesNotContain("refresh-secret", tokens.ToString());
        Assert.DoesNotContain(access.Value, new LoginResult(new(Guid.NewGuid(), "safe@a.com", UserRole.User), tokens).ToString());
        Assert.DoesNotContain("refresh-secret", new RegisterResult(new(Guid.NewGuid(), "safe@a.com", UserRole.User), tokens).ToString());
    }

    [Fact]
    public void Public_json_keeps_email_but_omits_access_token_value()
    {
        var access = new AccessToken("jwt-secret", "Bearer", DateTimeOffset.UtcNow.AddMinutes(5));
        var result = new LoginResult(
            new(Guid.NewGuid(), "person@example.com", UserRole.User),
            new(access, new RefreshTokenDto("refresh-secret")));

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("person@example.com", json);
        Assert.DoesNotContain("jwt-secret", json);
        Assert.Contains("refresh-secret", json);
    }

    [Fact]
    public void Sensitive_token_values_are_omitted_from_value_object_serialization()
    {
        var issuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var jwt = "eyJhbGciOiJIUzI1NiJ9.jwt-secret.signature";
        var refreshValue = "refresh-secret";
        var access = new AccessToken(jwt, "Bearer", issuedAt.AddMinutes(5));
        var refresh = new RefreshTokenValue(refreshValue, issuedAt.AddDays(1));
        var material = new RefreshTokenMaterial(refresh, "hash-secret", issuedAt, refresh.ExpiresAt!.Value);

        var json = string.Join('|', JsonSerializer.Serialize(access), JsonSerializer.Serialize(refresh), JsonSerializer.Serialize(material));

        Assert.DoesNotContain(jwt, json);
        Assert.DoesNotContain(refreshValue, json);
        Assert.DoesNotContain("hash-secret", json);
        Assert.Contains("Bearer", json);
        Assert.Contains("ExpiresAt", json);
    }

    [Fact]
    public void Audit_metadata_values_do_not_include_authentication_secrets()
    {
        var allowedKeys = new[] { "reason", "result", "ip", "userAgent", "sessionId", "keyId" };
        var allowedMetadata = new Dictionary<string, string?>
        {
            ["result"] = "success",
            ["sessionId"] = "session-2030",
            ["ip"] = "192.0.2.10",
            ["userAgent"] = "Aegis-Test/1.0"
        };
        var serialized = JsonSerializer.Serialize(allowedMetadata);

        Assert.All(allowedMetadata.Keys, key => Assert.Contains(key, allowedKeys));
        Assert.DoesNotContain("safe@a.com", serialized);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9.jwt-secret.signature", serialized);
        Assert.DoesNotContain("refresh-secret", serialized);
        Assert.DoesNotContain("cookie-secret", serialized);
        Assert.DoesNotContain("jwt-signing-secret", serialized);
    }
}
