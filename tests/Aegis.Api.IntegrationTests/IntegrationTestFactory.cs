using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Aegis.Api.IntegrationTests;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Dictionary<string, string?> _previousEnvironment = new();

    public IntegrationTestFactory(
        IReadOnlyDictionary<string, string?>? settings = null,
        string environment = "Development")
    {
        EnvironmentName = environment;
        PostgresContainerFixture.Current.ResetDatabaseAsync().GetAwaiter().GetResult();
        var resolved = settings is null ? TestSettings.Valid() : new Dictionary<string, string?>(settings);
        if (settings is null) resolved["RateLimiting:KeyPrefix"] = Guid.NewGuid().ToString("N");
        _settings = resolved;
        foreach (var setting in _settings)
        {
            var key = setting.Key.Replace(":", "__", StringComparison.Ordinal);
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, setting.Value);
        }
    }

    public string EnvironmentName { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("https_port", "443");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_settings));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var setting in _previousEnvironment)
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        base.Dispose(disposing);
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
