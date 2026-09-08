using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Aegis.Application;
using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Application.UseCases;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;
using Aegis.Infrastructure;
using Aegis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Infrastructure.IntegrationTests;

[Collection("Postgres")]
public sealed class AuthenticationUseCasePostgresTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Change_password_via_di_revokes_sessions_and_old_refresh()
    {
        await fixture.ResetDatabaseAsync();
        var email = $"change-password-{Guid.NewGuid():N}@example.com";
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var user = await SeedUser(scope.ServiceProvider, email, "old-password-123");
        var login = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginCommand(email, "old-password-123"));
        var oldRefresh = login.Value!.Tokens.RefreshToken.Value;
        SetCurrentUser(scope.ServiceProvider, user.Id);

        var changed = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
            .ExecuteAsync(new ChangePasswordCommand("old-password-123", "new-password-123"));
        Assert.True(changed.IsSuccess);

        var refresh = await scope.ServiceProvider.GetRequiredService<RefreshUseCase>()
            .ExecuteAsync(new RefreshCommand(oldRefresh));
        Assert.True(refresh.IsFailure);
        Assert.Equal(ApplicationErrorCode.SessionRevoked, refresh.ErrorCode);
        await AssertSessionsRevoked(user.Id, SessionRevocationReason.PasswordChanged);
    }

    [Fact]
    public async Task Deactivate_user_via_di_revokes_sessions_and_blocks_later_login()
    {
        await fixture.ResetDatabaseAsync();
        var email = $"deactivate-{Guid.NewGuid():N}@example.com";
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var user = await SeedUser(scope.ServiceProvider, email, "active-password-123");
        var login = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginCommand(email, "active-password-123"));
        var oldRefresh = login.Value!.Tokens.RefreshToken.Value;
        SetCurrentUser(scope.ServiceProvider, user.Id);

        var deactivated = await scope.ServiceProvider.GetRequiredService<DeactivateUserUseCase>().ExecuteAsync();
        Assert.True(deactivated.IsSuccess);

        var refresh = await scope.ServiceProvider.GetRequiredService<RefreshUseCase>()
            .ExecuteAsync(new RefreshCommand(oldRefresh));
        Assert.True(refresh.IsFailure);
        Assert.Equal(ApplicationErrorCode.SessionRevoked, refresh.ErrorCode);
        var laterLogin = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginCommand(email, "active-password-123"));
        Assert.True(laterLogin.IsFailure);
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, laterLogin.ErrorCode);
        await AssertSessionsRevoked(user.Id, SessionRevocationReason.UserDeactivated);
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Aegis"] = fixture.ConnectionString,
            ["Jwt:SecretKey"] = new string('x', 64), ["Jwt:Algorithm"] = "HS256",
            ["Jwt:Issuer"] = "Aegis.Api", ["Jwt:Audience"] = "Aegis.Client",
            ["Jwt:KeyId"] = "aegis-primary-01", ["Jwt:AccessTokenExpirationMinutes"] = "15",
            ["Jwt:RefreshTokenExpirationDays"] = "7", ["Jwt:ClockSkewSeconds"] = "30"
        }).Build();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new ServiceCollection().AddSingleton<IHttpContextAccessor>(http)
            .AddAegisApplication().AddAegisInfrastructure(configuration).BuildServiceProvider();
    }

    private static async Task<User> SeedUser(IServiceProvider services, string email, string password)
    {
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var user = new User(Guid.NewGuid(), new Email(email), hasher.Hash(password));
        await services.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    private static void SetCurrentUser(IServiceProvider services, Guid userId)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], "test"));
    }

    private async Task AssertSessionsRevoked(Guid userId, SessionRevocationReason reason)
    {
        await using var db = fixture.CreateDbContext();
        var persisted = await db.Sessions.Include(x => x.RefreshTokens).Where(x => x.UserId == userId).ToListAsync();
        Assert.NotEmpty(persisted);
        Assert.All(persisted, session =>
        {
            Assert.NotNull(session.RevokedAt);
            Assert.Equal((int)reason, session.RevocationReason);
            Assert.All(session.RefreshTokens, token => Assert.NotNull(token.RevokedAt));
        });
    }
}
