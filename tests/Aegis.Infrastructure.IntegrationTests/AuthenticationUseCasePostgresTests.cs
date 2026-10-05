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

    [Fact]
    public async Task Concurrent_login_and_deactivation_leave_no_active_session_after_deactivation()
    {
        await fixture.ResetDatabaseAsync();
        var email = $"concurrent-deactivate-{Guid.NewGuid():N}@example.com";
        const string password = "concurrent-password-123";

        await using var seedProvider = BuildProvider();
        await using var seedScope = seedProvider.CreateAsyncScope();
        var user = await SeedUser(seedScope.ServiceProvider, email, password);

        await using var loginProvider = BuildProvider();
        await using var deactivateProvider = BuildProvider();
        await using var loginScope = loginProvider.CreateAsyncScope();
        await using var deactivateScope = deactivateProvider.CreateAsyncScope();
        SetCurrentUser(deactivateScope.ServiceProvider, user.Id);

        var loginReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deactivateReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var loginTask = Task.Run(async () =>
        {
            loginReady.SetResult(true);
            await release.Task;
            return await loginScope.ServiceProvider.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginCommand(email, password));
        });
        var deactivateTask = Task.Run(async () =>
        {
            deactivateReady.SetResult(true);
            await release.Task;
            return await deactivateScope.ServiceProvider.GetRequiredService<DeactivateUserUseCase>().ExecuteAsync();
        });

        await Task.WhenAll(loginReady.Task, deactivateReady.Task);
        release.SetResult(true);
        await loginTask;
        var deactivateResult = await deactivateTask;

        Assert.True(deactivateResult.IsSuccess);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.Sessions.CountAsync(x => x.UserId == user.Id && x.RevokedAt == null));
    }

    [Fact]
    public async Task Register_via_di_characterizes_eleven_and_twelve_character_passwords()
    {
        await fixture.ResetDatabaseAsync();
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var register = scope.ServiceProvider.GetRequiredService<RegisterUseCase>();

        var shortResult = await register.ExecuteAsync(new RegisterCommand($"short-{Guid.NewGuid():N}@example.com", new string('p', 11)));
        var validResult = await register.ExecuteAsync(new RegisterCommand($"valid-{Guid.NewGuid():N}@example.com", new string('p', 12)));

        Assert.Equal(ApplicationErrorCode.WeakPassword, shortResult.ErrorCode);
        Assert.True(validResult.IsSuccess);
    }

    [Fact]
    public async Task Register_existing_email_is_rejected_before_insert()
    {
        await fixture.ResetDatabaseAsync();
        var email = $"existing-{Guid.NewGuid():N}@example.com";
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var register = scope.ServiceProvider.GetRequiredService<RegisterUseCase>();

        var first = await register.ExecuteAsync(new RegisterCommand(email, "existing-password-123"));
        var second = await register.ExecuteAsync(new RegisterCommand(email, "existing-password-456"));

        Assert.True(first.IsSuccess);
        Assert.Equal(ApplicationErrorCode.EmailAlreadyRegistered, second.ErrorCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(x => x.Email == email));
    }

    [Fact]
    public async Task Register_concurrent_same_email_returns_one_success_and_one_duplicate()
    {
        await fixture.ResetDatabaseAsync();
        var email = $"race-register-{Guid.NewGuid():N}@example.com";
        await using var firstProvider = BuildProvider();
        await using var secondProvider = BuildProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondScope = secondProvider.CreateAsyncScope();

        var first = firstScope.ServiceProvider.GetRequiredService<RegisterUseCase>();
        var second = secondScope.ServiceProvider.GetRequiredService<RegisterUseCase>();
        var results = await Task.WhenAll(
            first.ExecuteAsync(new RegisterCommand(email, "race-password-123")),
            second.ExecuteAsync(new RegisterCommand(email, "race-password-456")));

        Assert.Single(results, x => x.IsSuccess);
        Assert.Single(results, x => x.ErrorCode == ApplicationErrorCode.EmailAlreadyRegistered);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(x => x.Email == email));
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Aegis"] = fixture.ConnectionString,
            ["Jwt:SecretKey"] = new string('x', 64),
            ["Jwt:Algorithm"] = "HS256",
            ["Jwt:Issuer"] = "Aegis.Api",
            ["Jwt:Audience"] = "Aegis.Client",
            ["Jwt:KeyId"] = "aegis-primary-01",
            ["Jwt:AccessTokenExpirationMinutes"] = "15",
            ["Jwt:RefreshTokenExpirationDays"] = "7",
            ["Jwt:ClockSkewSeconds"] = "30"
        }).Build();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new ServiceCollection().AddSingleton<IHttpContextAccessor>(http)
            .AddAegisApplication().AddAegisInfrastructure(configuration).BuildServiceProvider();
    }

    private static async Task<User> SeedUser(IServiceProvider services, string email, string password)
    {
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var user = new User(Guid.NewGuid(), new Email(email), PasswordHash.Create(hasher.Hash(password)).Value!);
         var users = services.GetRequiredService<IUserRepository>();
         var unit = services.GetRequiredService<IUnitOfWork>();
         await unit.ExecuteInTransactionAsync(async ct =>
         {
             await users.AddAsync(user, ct);
             return new TransactionOutcome<bool>(true, TransactionDecision.Commit);
         });
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
