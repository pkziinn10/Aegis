using System.IdentityModel.Tokens.Jwt;
using Aegis.Application.Abstractions;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aegis.Infrastructure.IntegrationTests;

[Collection("Postgres")]
public sealed class SecurityInfrastructureTests : IClassFixture<PostgresContainerFixture>
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
            Issuer = "tests",
            Audience = "tests",
            Keys = [new JwtSigningKey { Kid = "2026-01", Secret = new string('x', 64), Current = true }]
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

    [Fact]
    public async Task Audit_writer_drops_sensitive_metadata_before_persisting()
    {
        var fixture = PostgresContainerFixture.Current;
        await using var db = fixture.CreateDbContext();
        var action = SecurityAuditAction.Login;
        var userId = Guid.NewGuid();

        var unitOfWork = new EfUnitOfWork(db);
        var auditWriter = new AuditWriter(db, new TestClock());
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await auditWriter.WriteAsync(new SecurityAuditEvent(action, userId, SecurityAuditResult.Success), ct);
            return new TransactionOutcome<bool>(true, TransactionDecision.Commit);
        });

        var persisted = await db.AuditEvents.SingleAsync(x => x.UserId == userId);
        Assert.Equal("{\"result\":\"success\"}", persisted.MetadataJson);

        var sessionId = Guid.NewGuid();
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await auditWriter.WriteAsync(new SecurityAuditEvent(SecurityAuditAction.RefreshTokenReuse, null, SecurityAuditResult.FamilyRevoked, sessionId), ct);
            return new TransactionOutcome<bool>(true, TransactionDecision.Commit);
        });

        var safePersisted = await db.AuditEvents.SingleAsync(x => x.Action == "refresh_token_reuse");
        Assert.Contains("family_revoked", safePersisted.MetadataJson);
        Assert.Contains(sessionId.ToString("N"), safePersisted.MetadataJson);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
