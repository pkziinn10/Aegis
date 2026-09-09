using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Api.IntegrationTests;

internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("RateLimiting__AccountKeySecret", TestSettings.Secret);
    }
}

[Collection("Postgres")]
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

[Collection("Postgres")]
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
    [InlineData("nbf-missing")]
    [InlineData("nbf-string")]
    [InlineData("nbf-non-numeric")]
    [InlineData("nbf-future")]
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
    public async Task Nbf_inside_configured_clock_skew_is_accepted()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("nbf-skew"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/integration/protected")).StatusCode);
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

    [Fact]
    public async Task Tokens_signed_with_current_and_previous_configured_keys_are_accepted()
    {
        var settings = TestSettings.With(
            ("Jwt:Keys:0:Kid", "aegis-primary-01"),
            ("Jwt:Keys:0:Secret", TestSettings.Secret),
            ("Jwt:Keys:0:Current", "true"),
            ("Jwt:Keys:1:Kid", "aegis-previous-01"),
            ("Jwt:Keys:1:Secret", "previous-integration-secret-with-at-least-32-bytes-012345"),
            ("Jwt:Keys:1:Current", "false"));
        using var factory = new IntegrationTestFactory(settings);
        using var client = factory.CreateHttpsClient();

        foreach (var key in new[] { ("aegis-primary-01", TestSettings.Secret), ("aegis-previous-01", "previous-integration-secret-with-at-least-32-bytes-012345") })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateTokenWithKey(key.Item1, key.Item2));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/integration/protected")).StatusCode);
        }
    }

    private static string CreateToken(string? invalidPart = null)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-5);
        if (invalidPart is "nbf-missing" or "nbf-string" or "nbf-non-numeric")
        {
            var nbf = invalidPart == "nbf-missing" ? null : invalidPart == "nbf-string" ? "now" : "not-a-number";
            return CreateRawToken(now, now.AddMinutes(5), nbf, invalidPart == "nbf-missing");
        }
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
        var notBefore = invalidPart == "nbf-future" ? now.AddSeconds(60).UtcDateTime
            : invalidPart == "nbf-skew" ? now.AddSeconds(10).UtcDateTime
            : invalidPart is "iat-after-exp" or "iat-future" ? now.UtcDateTime : issued.UtcDateTime;
        var token = new JwtSecurityToken(
            issuer, audience, claims,
            invalidPart == "exp-missing" ? null : notBefore,
            invalidPart == "exp-missing" ? null : expires.UtcDateTime, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateRawToken(DateTimeOffset issued, DateTimeOffset expires, string? nbf, bool omitNbf)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT", kid = "aegis-primary-01" }));
        var payload = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = "integration-user",
            [JwtRegisteredClaimNames.Iat] = issued.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Exp] = expires.ToUnixTimeSeconds()
        };
        if (!omitNbf) payload[JwtRegisteredClaimNames.Nbf] = nbf!;
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{header}.{encodedPayload}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestSettings.Secret), Encoding.UTF8.GetBytes(unsigned));
        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreateTokenWithKey(string kid, string secret)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-5);
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT", kid }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Iss] = "Aegis.Api",
            [JwtRegisteredClaimNames.Aud] = "Aegis.Client",
            [JwtRegisteredClaimNames.Sub] = "integration-user",
            [JwtRegisteredClaimNames.Iat] = now.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Nbf] = now.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Exp] = now.AddMinutes(5).ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        return $"{unsigned}.{Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(unsigned)))}";
    }

}

[Collection("Postgres")]
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

[Collection("Postgres")]
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
        var problemBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("RateLimitExceeded", problemBody.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problemBody.GetProperty("traceId").GetString()));

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

    [Fact]
    public async Task Login_limit_is_per_account_across_ips_and_does_not_cross_accounts()
    {
        using var factory = new IntegrationTestFactory(TestSettings.With(
            ("RateLimiting:LoginAccountPermitLimit", "2"),
            ("RateLimiting:LoginIpPermitLimit", "20")));
        using var client = factory.CreateHttpsClient();
        var first = $"rate-first-{Guid.NewGuid():N}@example.com";
        var second = $"rate-second-{Guid.NewGuid():N}@example.com";
        const string password = "rate-password-123";

        foreach (var email in new[] { first, second })
        {
            using var register = new HttpRequestMessage(HttpMethod.Post, "/auth/token/register")
            {
                Content = JsonContent.Create(new { email, password })
            };
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(register)).StatusCode);
        }

        foreach (var ip in new[] { "10.0.0.41", "10.0.0.42" })
        {
            using var login = Login(first, password, ip);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);
        }

        using var blocked = Login(first, password, "10.0.0.43");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(blocked)).StatusCode);

        using var otherAccount = Login(second, password, "10.0.0.43");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(otherAccount)).StatusCode);
    }

    private static HttpRequestMessage Login(string email, string password, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-Test-Remote-IP", ip);
        return request;
    }
}

[Collection("Postgres")]
public sealed class IdentityApiIntegrationTests
{
    [Fact]
    public async Task Token_logout_is_rate_limited_by_ip()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/logout");
            request.Headers.Add("X-Test-Remote-IP", "10.0.0.30");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        }

        using var sixth = new HttpRequestMessage(HttpMethod.Post, "/auth/token/logout");
        sixth.Headers.Add("X-Test-Remote-IP", "10.0.0.30");
        var response = await client.SendAsync(sixth);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_without_credentials_returns_unauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProblem(response, "Unauthorized", 401);
    }

    [Fact]
    public async Task Oversized_bearer_is_rejected_before_authentication()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string('x', 8 * 1024));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProblem(response, "InvalidBearerToken", 401);
    }

    [Fact]
    public async Task Oversized_auth_payload_is_rejected_before_password_processing()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/login")
        {
            Content = JsonContent.Create(new { email = "payload@example.com", password = new string('p', 17 * 1024) })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AssertProblem(response, "PayloadTooLarge", 413);
        Assert.DoesNotContain("InvalidPasswordHash", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_token_is_accepted_by_admin_endpoint()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateAdminToken());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/integration/role")).StatusCode);
    }

    [Fact]
    public async Task Browser_mutation_rejects_unlisted_origin_before_application()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/browser/login")
        {
            Content = JsonContent.Create(new { email = "user@example.com", password = "a-password-longer-than-12" })
        };
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertProblem(response, "InvalidRequest", 403);
    }

    private static string CreateAdminToken()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-5);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "integration-admin"),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString()),
            new Claim("roles", "admin")
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSettings.Secret)) { KeyId = "aegis-primary-01" },
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("Aegis.Api", "Aegis.Client", claims,
            now.UtcDateTime, now.AddMinutes(5).UtcDateTime, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertProblem(HttpResponseMessage response, string code, int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Browser_refresh_requires_origin_and_csrf()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/browser/refresh");
        request.Headers.Add("Origin", "https://localhost:5173");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("\"code\":\"InvalidRequest\"", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("InvalidPasswordHash", body);
    }

    [Fact]
    public async Task Invalid_json_returns_safe_problem_details()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token/login")
        {
            Content = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"code\":\"InvalidRequest\"", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("not-json", body);
    }
}

public sealed record RateLimitProblem(string Title, int Status);

[Collection("Postgres")]
public sealed class AuthenticationFlowIntegrationTests
{
    [Fact]
    public async Task Browser_register_emits_host_refresh_and_csrf_cookies_without_refresh_body()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"browser-register-{Guid.NewGuid():N}@example.com";

        using var request = Credentials(HttpMethod.Post, "/auth/browser/register", email);
        request.Headers.Add("Origin", "https://localhost:5173");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csrfToken", body, StringComparison.Ordinal);
        AssertCookie(response, "__Host-refresh-token", httpOnly: true);
        AssertCookie(response, "__Host-csrf-token", httpOnly: true);
    }

    [Fact]
    public async Task Browser_login_and_refresh_rotate_host_cookie_and_keep_refresh_out_of_body()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"browser-login-{Guid.NewGuid():N}@example.com";
        const string password = "browser-password-123";

        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email, password);
        var registered = await client.SendAsync(register);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        using var login = Credentials(HttpMethod.Post, "/auth/browser/login", email, password);
        login.Headers.Add("Origin", "https://localhost:5173");
        var loggedIn = await client.SendAsync(login);
        var loginBody = await loggedIn.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        Assert.DoesNotContain("refresh", loginBody, StringComparison.OrdinalIgnoreCase);

        var refreshCookie = CookieValue(loggedIn, "__Host-refresh-token");
        var csrfToken = JsonDocument.Parse(loginBody).RootElement.GetProperty("csrfToken").GetString()!;
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/browser/refresh");
        refresh.Headers.Add("Origin", "https://localhost:5173");
        refresh.Headers.Add("X-CSRF-TOKEN", csrfToken);
        refresh.Headers.Add("Cookie", $"__Host-refresh-token={refreshCookie}");

        var rotated = await client.SendAsync(refresh);
        var rotatedBody = await rotated.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.DoesNotContain("refresh", rotatedBody, StringComparison.OrdinalIgnoreCase);
        var rotatedCookie = CookieValue(rotated, "__Host-refresh-token");
        Assert.NotEqual(refreshCookie, rotatedCookie);
        AssertCookie(rotated, "__Host-refresh-token", httpOnly: true);
    }

    [Fact]
    public async Task Browser_logout_clears_refresh_and_csrf_cookies()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"browser-logout-{Guid.NewGuid():N}@example.com";
        const string password = "browser-password-123";

        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email, password);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(register)).StatusCode);
        using var login = Credentials(HttpMethod.Post, "/auth/browser/login", email, password);
        login.Headers.Add("Origin", "https://localhost:5173");
        var loggedIn = await client.SendAsync(login);
        var csrf = JsonDocument.Parse(await loggedIn.Content.ReadAsStringAsync()).RootElement.GetProperty("csrfToken").GetString()!;
        var refresh = CookieValue(loggedIn, "__Host-refresh-token");

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/browser/logout");
        logout.Headers.Add("Origin", "https://localhost:5173");
        logout.Headers.Add("X-CSRF-TOKEN", csrf);
        logout.Headers.Add("Cookie", $"__Host-refresh-token={refresh}; __Host-csrf-token={csrf}");
        var response = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDeletedCookie(response, "__Host-refresh-token");
        AssertDeletedCookie(response, "__Host-csrf-token");
    }

    [Fact]
    public async Task Browser_terminal_refresh_failure_clears_refresh_and_csrf_cookies()
    {
        using var factory = new IntegrationTestFactory();
        using var client = new HttpClient(factory.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
        var email = $"browser-terminal-refresh-{Guid.NewGuid():N}@example.com";
        const string password = "browser-password-123";

        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email, password);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(register)).StatusCode);
        using var login = Credentials(HttpMethod.Post, "/auth/browser/login", email, password);
        login.Headers.Add("Origin", "https://localhost:5173");
        var loggedIn = await client.SendAsync(login);
        var csrf = JsonDocument.Parse(await loggedIn.Content.ReadAsStringAsync()).RootElement.GetProperty("csrfToken").GetString()!;
        var csrfCookie = CookieValue(loggedIn, "__Host-csrf-token");
        var refresh = CookieValue(loggedIn, "__Host-refresh-token");

        using var firstRefresh = BrowserRefresh(refresh, csrf, csrfCookie);
        var firstResponse = await client.SendAsync(firstRefresh);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var rotatedCsrf = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("csrfToken").GetString()!;
        var rotatedCsrfCookie = csrfCookie;

        using var terminalRefresh = BrowserRefresh(refresh, rotatedCsrf, rotatedCsrfCookie);
        var response = await client.SendAsync(terminalRefresh);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProblem(response, "RefreshTokenReuse", 401);
        AssertDeletedCookie(response, "__Host-refresh-token");
        AssertDeletedCookie(response, "__Host-csrf-token");
    }

    private static HttpRequestMessage BrowserRefresh(string refresh, string csrf, string csrfCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/browser/refresh");
        request.Headers.Add("Origin", "https://localhost:5173");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", $"__Host-refresh-token={refresh}; __Host-csrf-token={csrfCookie}");
        return request;
    }

    [Fact]
    public async Task Token_register_login_and_refresh_return_refresh_json()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"token-flow-{Guid.NewGuid():N}@example.com";
        const string password = "token-password-123";

        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email, password);
        var registered = await client.SendAsync(register);
        var registeredBody = JsonDocument.Parse(await registered.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var registeredRefresh = registeredBody.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(registeredRefresh));

        using var login = Credentials(HttpMethod.Post, "/auth/token/login", email, password);
        var loggedIn = await client.SendAsync(login);
        var loginBody = JsonDocument.Parse(await loggedIn.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var loginRefresh = loginBody.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(loginRefresh));

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/token/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = loginRefresh })
        };
        var refreshed = await client.SendAsync(refresh);
        var refreshedBody = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(refreshedBody.GetProperty("refreshToken").GetString()));
        Assert.DoesNotContain("__Host-refresh-token", refreshed.Headers.SelectMany(x => x.Value));

        using var reused = new HttpRequestMessage(HttpMethod.Post, "/auth/token/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = loginRefresh })
        };
        var reuseResponse = await client.SendAsync(reused);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
        AssertProblem(reuseResponse, "RefreshTokenReuse", 401);

        await using var db = PostgresContainerFixture.Current.CreateDbContext();
        var session = await db.Sessions.Include(x => x.RefreshTokens)
            .SingleAsync(x => x.RefreshTokens.Any(t => t.Hash == Hash(loginRefresh!)));
        Assert.NotNull(session.RevokedAt);
        Assert.Equal((int)Aegis.Domain.Enums.SessionRevocationReason.RefreshTokenReuse, session.RevocationReason);
        Assert.All(session.RefreshTokens, token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public async Task Token_logout_revokes_refresh_and_persists_manual_reason()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"token-logout-{Guid.NewGuid():N}@example.com";
        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email);
        var registered = await client.SendAsync(register);
        var refreshToken = JsonDocument.Parse(await registered.Content.ReadAsStringAsync())
            .RootElement.GetProperty("refreshToken").GetString()!;

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/token/logout")
        {
            Content = JsonContent.Create(new { refreshToken })
        };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/token/refresh")
        {
            Content = JsonContent.Create(new { refreshToken })
        };
        var rejected = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        AssertProblem(rejected, "SessionRevoked", 401);

        await using var db = PostgresContainerFixture.Current.CreateDbContext();
        var session = await db.Sessions.Include(x => x.RefreshTokens)
            .SingleAsync(x => x.RefreshTokens.Any(t => t.Hash == Hash(refreshToken)));
        Assert.NotNull(session.RevokedAt);
        Assert.Equal((int)Aegis.Domain.Enums.SessionRevocationReason.Manual, session.RevocationReason);
        Assert.All(session.RefreshTokens, token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public async Task Me_returns_registered_user_with_real_access_token()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"me-{Guid.NewGuid():N}@example.com";
        using var register = Credentials(HttpMethod.Post, "/auth/token/register", email);
        var registered = await client.SendAsync(register);
        var body = JsonDocument.Parse(await registered.Content.ReadAsStringAsync()).RootElement;
        var accessToken = body.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("User", me.GetProperty("role").GetString());
    }

    private static HttpRequestMessage Credentials(HttpMethod method, string path, string email, string password = "password-123456") => new(method, path)
    {
        Content = JsonContent.Create(new { email, password })
    };

    private static string CookieValue(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal))
            .Substring(name.Length + 1);

    private static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static void AssertProblem(HttpResponseMessage response, string code, int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static void AssertCookie(HttpResponseMessage response, string name, bool httpOnly)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("Secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", cookie, StringComparison.OrdinalIgnoreCase);
        if (httpOnly) Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        else Assert.DoesNotContain("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertDeletedCookie(HttpResponseMessage response, string name)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
