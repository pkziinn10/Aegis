using Aegis.Application.Abstractions;
using Aegis.Domain.Repositories;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aegis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAegisInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Aegis");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:Aegis is required.");
        services.AddDbContext<AegisDbContext>(o => o.UseNpgsql(connection));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<Argon2Options>(configuration.GetSection("Argon2"));
        services.AddOptions<JwtOptions>()
            .Validate(JwtOptions.HasMinimumSecretLength, "JWT secret must contain at least 32 UTF-8 bytes.")
            .Validate(x => x.Algorithm == Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256, "Only HS256 is supported.")
            .Validate(x => x.Issuer == "Aegis.Api", "JWT issuer must be Aegis.Api.")
            .Validate(x => x.Audience == "Aegis.Client", "JWT audience must be Aegis.Client.")
            .Validate(x => x.Keys.Count > 0 || x.KeyId == "aegis-primary-01", "JWT key id must be aegis-primary-01.")
            .Validate(JwtOptions.HasValidExpirationWindow, "Access token expiration must be between 1 and 15 minutes.")
            .Validate(x => x.RefreshTokenExpirationDays is > 0 and <= 7, "Refresh token expiration must be between 1 and 7 days.")
            .Validate(x => x.ClockSkewSeconds is >= 0 and <= 30, "Clock skew must be between 0 and 30 seconds.")
            .Validate(x => x.IsValid(), "JWT keyring/configuration is invalid.")
            .ValidateOnStart();
        var argon = configuration.GetSection("Argon2").Get<Argon2Options>() ?? new();
        if (argon.MemoryKiB < 8192 || argon.Iterations < 1 || argon.Parallelism < 1 || argon.SaltBytes < 16 || argon.HashBytes < 16) throw new InvalidOperationException("Argon2 configuration is invalid.");
        services.AddScoped<IUserRepository, UserRepository>().AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<TransactionRunner>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IRefreshTokenFactory, Sha256RefreshTokenFactory>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IRefreshTokenPolicy>(sp =>
            new RefreshTokenPolicy(sp.GetRequiredService<IOptions<JwtOptions>>().Value.RefreshTokenExpirationDays));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        return services;
    }
}
