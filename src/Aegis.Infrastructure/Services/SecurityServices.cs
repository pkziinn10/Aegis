using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aegis.Application.Abstractions;
using Aegis.Domain.Enums;
using Konscious.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Aegis.Infrastructure.Services;

public sealed class Argon2Options { public int MemoryKiB { get; set; } = 65536; public int Iterations { get; set; } = 3; public int Parallelism { get; set; } = 2; public int SaltBytes { get; set; } = 16; public int HashBytes { get; set; } = 32; }
public sealed class Argon2idPasswordHasher(Microsoft.Extensions.Options.IOptions<Argon2Options>? configured = null) : IPasswordHasher
{
    private Argon2Options Settings => configured?.Value ?? new();
    private readonly Lazy<string> dummyHash = new(() => HashCore("invalid-password", RandomNumberGenerator.GetBytes((configured?.Value ?? new()).SaltBytes), configured?.Value ?? new()), LazyThreadSafetyMode.ExecutionAndPublication);
    public string DummyHash => dummyHash.Value;
    public string Hash(string password) => HashCore(password, RandomNumberGenerator.GetBytes(Settings.SaltBytes), Settings);
    public bool Verify(string password, string passwordHash)
    {
        try
        {
            if (!TryParse(passwordHash, out var settings, out var salt, out var expected)) return false;
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashCore(password, salt, settings).Split('$')[5]), expected);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { return false; }
    }
    private static string HashCore(string password, byte[] salt, Argon2Options settings)
    {
        var argon = new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = settings.MemoryKiB, Iterations = settings.Iterations, DegreeOfParallelism = settings.Parallelism };
        return $"$argon2id$v=19$m={settings.MemoryKiB},t={settings.Iterations},p={settings.Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(argon.GetBytes(settings.HashBytes))}";
    }
    private static bool TryParse(string value, out Argon2Options settings, out byte[] salt, out byte[] expected)
    {
        settings = new(); salt = []; expected = [];
        var parts = value.Split('$'); if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19") return false;
        var parameters = parts[3].Split(','); if (parameters.Length != 3) return false;
        var values = parameters.Select(x => x.Split('=')).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : string.Empty, StringComparer.Ordinal);
        if (values.Count != 3 || !values.TryGetValue("m", out var m) || !values.TryGetValue("t", out var t) || !values.TryGetValue("p", out var p) || !int.TryParse(m, out var memory) || !int.TryParse(t, out var iterations) || !int.TryParse(p, out var parallelism)) return false;
        salt = Convert.FromBase64String(parts[4]); expected = Convert.FromBase64String(parts[5]);
        if (memory is < 8192 or > 1_048_576 || iterations is < 1 or > 20 || parallelism is < 1 or > 32 || salt.Length is < 16 or > 1024 || expected.Length is < 16 or > 1024) return false;
        settings = new Argon2Options { MemoryKiB = memory, Iterations = iterations, Parallelism = parallelism, SaltBytes = salt.Length, HashBytes = expected.Length }; return true;
    }
}

public sealed class Sha256RefreshTokenFactory : IRefreshTokenFactory
{
    public RefreshTokenMaterial Create(Guid sessionId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    { var value = new RefreshTokenValue(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('='), expiresAt); return new(value, Hash(value), createdAt, expiresAt); }
    public string Hash(RefreshTokenValue token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value))).ToLowerInvariant();
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "Aegis.Api";
    public string Audience { get; set; } = "Aegis.Client";
    public string Algorithm { get; set; } = SecurityAlgorithms.HmacSha256;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
    public int ClockSkewSeconds { get; set; } = 30;
    public string SecretKey { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public List<JwtSigningKey> Keys { get; set; } = [];
    public JwtSigningKey CurrentKey => Keys.Single(x => x.Current);
    public bool IsValid() { if (Keys.Count == 0 && !string.IsNullOrWhiteSpace(SecretKey)) Keys = [new JwtSigningKey { Kid = KeyId, Secret = SecretKey, Current = true }]; var kids = Keys.Select(x => x.Kid).ToArray(); return Algorithm == SecurityAlgorithms.HmacSha256 && Keys.Count > 0 && Keys.Count(x => x.Current) == 1 && kids.Distinct(StringComparer.Ordinal).Count() == kids.Length && Keys.All(x => !string.IsNullOrWhiteSpace(x.Kid) && Encoding.UTF8.GetByteCount(x.Secret) >= 32) && AccessTokenExpirationMinutes is > 0 and <= 15 && RefreshTokenExpirationDays is > 0 and <= 7 && ClockSkewSeconds is >= 0 and <= 30; }
    public static bool HasMinimumSecretLength(JwtOptions x) => Encoding.UTF8.GetByteCount(x.SecretKey) >= 32 || x.Keys.Any(k => Encoding.UTF8.GetByteCount(k.Secret) >= 32);
    public static bool HasValidExpirationWindow(JwtOptions x) => x.AccessTokenExpirationMinutes is > 0 and <= 15;
    public static bool TryReadNumericDate(string? value, out long seconds) => long.TryParse(value, out seconds) && seconds >= 0;
}
public sealed class JwtSigningKey { public string Kid { get; set; } = string.Empty; public string Secret { get; set; } = string.Empty; public bool Current { get; set; } }

public sealed class JwtAccessTokenIssuer(Microsoft.Extensions.Options.IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    public AccessToken Issue(Guid userId, UserRole role, DateTimeOffset issuedAt)
    {
        var o = options.Value; var key = o.CurrentKey; var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key.Secret)), SecurityAlgorithms.HmacSha256);
        var expires = issuedAt.AddMinutes(o.AccessTokenExpirationMinutes); var token = new JwtSecurityToken(o.Issuer, o.Audience, [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("roles", role.ToString().ToLowerInvariant()), new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)], issuedAt.UtcDateTime, expires.UtcDateTime, credentials);
        token.Header["kid"] = key.Kid;
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires);
    }
}

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class CurrentUserContext(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : ICurrentUserContext
{
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
