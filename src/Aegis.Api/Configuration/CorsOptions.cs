namespace Aegis.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];

    public static bool IsValid(CorsOptions options)
    {
        if (options.AllowedOrigins.Length == 0) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in options.AllowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin) || origin.Contains('*')
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
                || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || uri.AbsolutePath != "/" || origin.EndsWith("/", StringComparison.Ordinal)
                || !seen.Add(origin)) return false;
        }
        return true;
    }
}
