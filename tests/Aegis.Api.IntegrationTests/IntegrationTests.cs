using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aegis.Api.IntegrationTests;

public sealed class StartupConfigurationTests
{
    [Theory]
    [InlineData("Jwt:SecretKey", "", "JWT secret")]
    [InlineData("Jwt:SecretKey", "short", "JWT secret")]
    [InlineData("Jwt:Algorithm", "HS512", "Only HS256")]
    [InlineData("Jwt:Issuer", "wrong", "JWT issuer")]
    [InlineData("Jwt:Audience", "wrong", "JWT audience")]
    [InlineData("Jwt:KeyId", "wrong", "JWT key id")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "0", "Access token")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "16", "Access token")]
    [InlineData("Jwt:RefreshTokenExpirationDays", "0", "Refresh token")]
    [InlineData("Jwt:RefreshTokenExpirationDays", "8", "Refresh token")]
    [InlineData("Jwt:ClockSkewSeconds", "-1", "Clock skew")]
    [InlineData("Jwt:ClockSkewSeconds", "31", "Clock skew")]
    public void Invalid_jwt_configuration_fails_startup(string key, string value, string expectedMessage)
    {
        using var factory = new IntegrationTestFactory(TestSettings.With((key, value)));
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = Flatten(exception);
        if (key == "Jwt:SecretKey" && value.Length == 0)
        {
            var validation = Find<OptionsValidationException>(exception);
            Assert.NotNull(validation);
            Assert.Contains(expectedMessage, validation!.Message);
        }
        else Assert.Contains(expectedMessage, message);
    }

    [Theory]
    [InlineData("Cors:AllowedOrigins:0", "http://localhost:5173", "Cors")]
    [InlineData("Cors:AllowedOrigins:0", "https://localhost:5173/path", "Cors")]
    [InlineData("Cors:AllowedOrigins:1", "https://localhost:5173", "Cors")]
    [InlineData("AllowedHosts", "*", "AllowedHosts")]
    [InlineData("AllowedHosts", "localhost;", "AllowedHosts")]
    [InlineData("AllowedHosts", "localhost localhost", "AllowedHosts")]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip", "ReverseProxy")]
    public void Invalid_non_jwt_configuration_fails_startup(string key, string value, string expectedMessage)
    {
        var settings = TestSettings.With((key, value));
        if (key == "ReverseProxy:KnownProxies:0")
            settings["ReverseProxy:Enabled"] = "true";
        if (key == "ReverseProxy:Enabled")
            settings["ReverseProxy:KnownProxies:0"] = "127.0.0.1";
        using var factory = new IntegrationTestFactory(settings);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(expectedMessage, Flatten(exception));
    }

    private static string Flatten(Exception exception) =>
        string.Join(" | ", exception.GetBaseException().Message, exception.Message);

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is T match) return match;
        return null;
    }
}

public sealed class JwtIntegrationTests
{
    [Fact]
    public async Task Valid_token_is_accepted()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/integration/protected",
            new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken());
        response = await client.GetAsync("/integration/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("algorithm")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("exp-missing")]
    [InlineData("kid-missing")]
    [InlineData("kid-unknown")]
    [InlineData("sub-empty")]
    [InlineData("iat-missing")]
    [InlineData("iat-future")]
    [InlineData("iat-after-exp")]
    [InlineData("duration")]
    public async Task Invalid_token_is_rejected(string invalidPart)
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(invalidPart));
        var response = await client.GetAsync("/integration/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Role_claim_is_enforced()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/integration/role")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("role"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/integration/role")).StatusCode);
    }

    private static string CreateToken(string? invalidPart = null)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-5);
        var issued = invalidPart == "iat-future" ? now.AddSeconds(60)
            : invalidPart == "iat-after-exp" ? now.AddMinutes(10)
            : invalidPart == "expired" ? now.AddMinutes(-2) : now;
        var expires = invalidPart == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5);
        if (invalidPart == "duration") expires = now.AddMinutes(16);
        var keyId = invalidPart == "kid-unknown" ? "wrong" : invalidPart == "kid-missing" ? null : "aegis-primary-01";
        var claims = new List<Claim>();
        claims.Add(new(JwtRegisteredClaimNames.Sub, invalidPart == "sub-empty" ? "" : "integration-user"));
        if (!string.Equals(invalidPart, "iat-missing", StringComparison.Ordinal))
            claims.Add(new(JwtRegisteredClaimNames.Iat, issued.ToUnixTimeSeconds().ToString()));
        if (invalidPart == "role") claims.Add(new("roles", "admin"));
        var issuer = invalidPart == "issuer" ? "wrong" : "Aegis.Api";
        var audience = invalidPart == "audience" ? "wrong" : "Aegis.Client";
        var secret = invalidPart == "signature"
            ? "wrong-signing-secret-with-at-least-32-bytes"
            : TestSettings.Secret;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = keyId },
            invalidPart == "algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256);
        var notBefore = invalidPart is "iat-after-exp" or "iat-future"
            ? now.UtcDateTime : issued.UtcDateTime;
        var token = new JwtSecurityToken(
            issuer, audience, claims,
            invalidPart == "exp-missing" ? null : notBefore,
            invalidPart == "exp-missing" ? null : expires.UtcDateTime, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}

public sealed class WebSecurityIntegrationTests
{
    [Fact]
    public async Task Cors_allows_configured_origin_and_blocks_other_origin()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var allowed = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        allowed.Headers.Add("Origin", "https://localhost:5173");
        var response = await client.SendAsync(allowed);
        Assert.Equal("https://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var denied = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        denied.Headers.Add("Origin", "https://evil.example");
        response = await client.SendAsync(denied);
        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(x => x.Key));
    }

    [Fact]
    public async Task Cors_preflight_is_allowed()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/integration/public");
        request.Headers.Add("Origin", "https://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task Cookie_policy_forces_secure_httponly_and_strict()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/integration/cookie");
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_has_hsts_and_http_redirects_to_https()
    {
        using var factory = new IntegrationTestFactory(
            TestSettings.With(("AllowedHosts", "localhost;127.0.0.1;example.test")),
            "Production");
        using var httpsClient = factory.CreateHttpsClient();
        using var httpsRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        httpsRequest.Headers.Add("X-Test-Scheme", "https");
        httpsRequest.Headers.Host = "example.test";
        var hstsResponse = await httpsClient.SendAsync(httpsRequest);
        Assert.Equal(HttpStatusCode.OK, hstsResponse.StatusCode);
        var hsts = hstsResponse.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=2592000", hsts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("includeSubDomains", hsts, StringComparison.OrdinalIgnoreCase);

        using var redirectFactory = new IntegrationTestFactory(environment: "Production");
        using var httpClient = redirectFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        httpRequest.Headers.Add("X-Test-Scheme", "http");
        var httpResponse = await httpClient.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, httpResponse.StatusCode);
        Assert.StartsWith("https://", httpResponse.Headers.Location?.ToString());

        using var development = new IntegrationTestFactory();
        using var developmentClient = development.CreateHttpsClient();
        var developmentResponse = await developmentClient.GetAsync("/integration/public");
        Assert.DoesNotContain("Strict-Transport-Security", developmentResponse.Headers.Select(x => x.Key));
    }

    [Fact]
    public async Task Forwarded_headers_are_applied_only_for_known_proxy()
    {
        var settings = TestSettings.With(
            ("ReverseProxy:Enabled", "true"),
            ("ReverseProxy:KnownProxies:0", "127.0.0.1"));
        using var factory = new IntegrationTestFactory(settings);
        using var client = factory.CreateHttpsClient();
        using var trusted = new HttpRequestMessage(HttpMethod.Get, "/integration/request-info");
        trusted.Headers.Add("X-Test-Remote-IP", "127.0.0.1");
        trusted.Headers.Add("X-Test-Scheme", "http");
        trusted.Headers.Add("X-Forwarded-Proto", "https");
        trusted.Headers.Add("X-Forwarded-For", "10.0.0.8");
        var response = await client.SendAsync(trusted);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("https", body.GetProperty("scheme").GetString());
        Assert.Equal("10.0.0.8", body.GetProperty("ip").GetString());

        using var unknownFactory = new IntegrationTestFactory(TestSettings.With(
            ("ReverseProxy:Enabled", "true"),
            ("ReverseProxy:KnownProxies:0", "192.0.2.1")));
        using var unknownClient = unknownFactory.CreateHttpsClient();
        using var unknown = new HttpRequestMessage(HttpMethod.Get, "/integration/request-info");
        unknown.Headers.Add("X-Test-Remote-IP", "127.0.0.1");
        unknown.Headers.Add("X-Forwarded-Proto", "https");
        unknown.Headers.Add("X-Forwarded-For", "10.0.0.9");
        unknown.Headers.Add("X-Test-Scheme", "http");
        response = await unknownClient.SendAsync(unknown);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith("https://", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Host_filter_allows_configured_hosts_rejects_unknown_and_accepts_ipv6()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        allowed.Headers.Host = "localhost";
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(allowed)).StatusCode);

        using var denied = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        denied.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(denied)).StatusCode);

        using var ipv6Factory = new IntegrationTestFactory(TestSettings.With(
            ("AllowedHosts", "localhost;127.0.0.1;[::1]")));
        using var ipv6Client = ipv6Factory.CreateHttpsClient();
        using var ipv6 = new HttpRequestMessage(HttpMethod.Get, "/integration/public");
        ipv6.Headers.Host = "[::1]";
        Assert.Equal(HttpStatusCode.OK, (await ipv6Client.SendAsync(ipv6)).StatusCode);
    }
}

public sealed class RateLimitIntegrationTests
{
    [Fact]
    public async Task AuthByIp_allows_five_and_rejects_sixth_request()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/integration/limited");
            request.Headers.Add("X-Test-Remote-IP", "10.0.0.1");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        using var sixth = new HttpRequestMessage(HttpMethod.Post, "/integration/limited");
        sixth.Headers.Add("X-Test-Remote-IP", "10.0.0.1");
        var rejected = await client.SendAsync(sixth);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var response = rejected;
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonSerializer.Deserialize<RateLimitProblem>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("Too Many Requests", problem?.Title);
        Assert.Equal(429, problem?.Status);

        using var otherIp = new HttpRequestMessage(HttpMethod.Post, "/integration/limited");
        otherIp.Headers.Add("X-Test-Remote-IP", "10.0.0.2");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(otherIp)).StatusCode);
    }

    [Fact]
    public async Task Endpoint_without_policy_is_not_limited()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/integration/public")).StatusCode);
    }
}

public sealed record RateLimitProblem(string Title, int Status);
