using System.Net;

namespace Aegis.Api.Configuration;

public static class AllowedHostsOptions
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var hosts = value.Split(';', StringSplitOptions.None);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
        {
            var isBracketedIpv6 = host.StartsWith("[", StringComparison.Ordinal)
                && host.EndsWith("]", StringComparison.Ordinal)
                && IPAddress.TryParse(host[1..^1], out var bracketedAddress)
                && bracketedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            var isIpAddress = IPAddress.TryParse(host, out _)
                || isBracketedIpv6;

            if (string.IsNullOrWhiteSpace(host) || host == "*"
                || host.ContainsAny(['/', '?', '#', ' '])
                || (!isIpAddress && host.Contains(':'))
                || !seen.Add(host)
                || (!isIpAddress && Uri.CheckHostName(host) == UriHostNameType.Unknown))
            {
                return false;
            }
        }
        return true;
    }
}
