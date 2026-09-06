using System.Text;
using System.Globalization;

namespace Aegis.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; init; } = string.Empty;

    public string Algorithm { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string KeyId { get; init; } = string.Empty;

    public int AccessTokenExpirationMinutes { get; init; }

    public int RefreshTokenExpirationDays { get; init; }

    public int ClockSkewSeconds { get; init; }

    public static bool HasMinimumSecretLength(JwtOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.SecretKey)
            && Encoding.UTF8.GetByteCount(options.SecretKey) >= 32;
    }

    public static bool HasValidExpirationWindow(JwtOptions options)
    {
        return options.AccessTokenExpirationMinutes is > 0 and <= 15;
    }

    public static bool TryReadNumericDate(string? value, out long unixSeconds)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out unixSeconds)
            && unixSeconds >= 0;
    }
}
