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
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<Argon2Options>(configuration.GetSection("Argon2"));
        services.AddOptions<JwtOptions>().Validate(x => x.IsValid(), "Jwt configuration is invalid.").ValidateOnStart();
        var argon = configuration.GetSection("Argon2").Get<Argon2Options>() ?? new();
        if (argon.MemoryKiB < 8192 || argon.Iterations < 1 || argon.Parallelism < 1 || argon.SaltBytes < 16 || argon.HashBytes < 16) throw new InvalidOperationException("Argon2 configuration is invalid.");
        services.AddScoped<IUserRepository, UserRepository>().AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<TransactionRunner>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IRefreshTokenFactory, Sha256RefreshTokenFactory>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        return services;
    }
}
