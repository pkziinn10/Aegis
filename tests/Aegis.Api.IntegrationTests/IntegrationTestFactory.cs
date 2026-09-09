using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Aegis.Api.IntegrationTests;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public IntegrationTestFactory(
        IReadOnlyDictionary<string, string?>? settings = null,
        string environment = "Development")
    {
        EnvironmentName = environment;
        PostgresContainerFixture.Current.ResetDatabaseAsync().GetAwaiter().GetResult();
        var resolved = settings is null ? TestSettings.Valid() : new Dictionary<string, string?>(settings);
        if (settings is null) resolved["RateLimiting:KeyPrefix"] = Guid.NewGuid().ToString("N");
        resolved["https_port"] = "443";
        _settings = resolved;
    }

    public string EnvironmentName { get; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureHostConfiguration(configuration =>
            configuration.AddInMemoryCollection(_settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(_settings);
        });
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly);
            services.AddTransient<IStartupFilter, TestSchemeStartupFilter>();
        });
    }

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

}

internal sealed class TestSchemeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Scheme", out var scheme))
                context.Request.Scheme = scheme.ToString();
            if (context.Request.Headers.TryGetValue("X-Test-Remote-IP", out var ip)
                && System.Net.IPAddress.TryParse(ip.ToString(), out var address))
                context.Connection.RemoteIpAddress = address;
            await continuation();
        });
        next(app);
    };
}

public static class TestSettings
{
    public const string Secret = "integration-test-secret-with-at-least-64-bytes-for-hs512-validation-0123456789";

    public static Dictionary<string, string?> Valid(string? secret = null) => new()
    {
        ["ConnectionStrings:Aegis"] = PostgresContainerFixture.Current.ConnectionString,
        ["RateLimiting:RedisConnection"] = PostgresContainerFixture.Current.RedisConnectionString,
        ["RateLimiting:AccountKeySecret"] = Secret,
        ["RateLimiting:KeyPrefix"] = Guid.NewGuid().ToString("N"),
        ["Jwt:SecretKey"] = secret ?? Secret,
        ["Jwt:Algorithm"] = "HS256",
        ["Jwt:Issuer"] = "Aegis.Api",
        ["Jwt:Audience"] = "Aegis.Client",
        ["Jwt:KeyId"] = "aegis-primary-01",
        ["Jwt:AccessTokenExpirationMinutes"] = "15",
        ["Jwt:RefreshTokenExpirationDays"] = "7",
        ["Jwt:ClockSkewSeconds"] = "30",
        ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
        ["ReverseProxy:Enabled"] = "false",
        ["DevelopmentSeed:Enabled"] = "false",
        ["AllowedHosts"] = "localhost;127.0.0.1"
    };

    public static Dictionary<string, string?> With(params (string Key, string? Value)[] values)
    {
        var result = Valid();
        foreach (var (key, value) in values)
            result[key] = value;
        return result;
    }
}
