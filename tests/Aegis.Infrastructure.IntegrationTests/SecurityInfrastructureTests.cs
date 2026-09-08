using System.IdentityModel.Tokens.Jwt;
using Aegis.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Aegis.Infrastructure.IntegrationTests;

public sealed class SecurityInfrastructureTests
{
    [Fact]
    public void Argon2id_hash_verifies_without_exposing_password()
    {
        var hasher = new Argon2idPasswordHasher();
        var hash = hasher.Hash("correct horse battery staple");
        Assert.StartsWith("$argon2id$", hash);
        Assert.True(hasher.Verify("correct horse battery staple", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.DoesNotContain("correct horse", hash);
    }

    [Fact]
    public void Refresh_factory_uses_one_way_sha256_hash()
    {
        var factory = new Sha256RefreshTokenFactory();
        var material = factory.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(64, material.Hash.Length);
        Assert.Equal(material.Hash, factory.Hash(material.Value));
        Assert.DoesNotContain(material.Value.Value, material.Hash);
    }

    [Fact]
    public void Jwt_uses_current_key_kid_and_hs256()
    {
        var issuer = new JwtAccessTokenIssuer(Options.Create(new JwtOptions
        {
            Issuer = "tests", Audience = "tests", Keys = [new JwtSigningKey { Kid = "2026-01", Secret = new string('x', 64), Current = true }]
        }));
        var token = issuer.Issue(Guid.NewGuid(), Aegis.Domain.Enums.UserRole.User, DateTimeOffset.UtcNow);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal("2026-01", parsed.Header.Kid);
        Assert.Equal("HS256", parsed.Header.Alg);
        Assert.DoesNotContain("[REDACTED", token.Value);
    }

    [Fact]
    public void Jwt_role_claim_is_lowercase()
    {
        var issuer = new JwtAccessTokenIssuer(Options.Create(new JwtOptions
        {
            Keys = [new JwtSigningKey { Kid = "2026-01", Secret = new string('x', 64), Current = true }]
        }));

        var token = issuer.Issue(Guid.NewGuid(), Aegis.Domain.Enums.UserRole.User, DateTimeOffset.UtcNow);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        Assert.Equal("user", parsed.Claims.Single(x => x.Type == "roles").Value);
    }
}
