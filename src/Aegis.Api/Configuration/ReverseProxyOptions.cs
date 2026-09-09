using System.Net;

namespace Aegis.Api.Configuration;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";
    public bool Enabled { get; init; }
    public string[] KnownProxies { get; init; } = [];

    public static bool IsValid(ReverseProxyOptions options)
    {
        return !options.Enabled || (options.KnownProxies.Length > 0
            && options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _))
            && options.KnownProxies.Distinct(StringComparer.Ordinal).Count() == options.KnownProxies.Length);
    }
}
