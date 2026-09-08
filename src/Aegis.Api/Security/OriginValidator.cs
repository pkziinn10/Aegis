using Aegis.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Aegis.Api.Security;

public sealed class OriginValidator(IOptions<CorsOptions> options)
{
    public bool IsAllowed(string? origin) => !string.IsNullOrWhiteSpace(origin) && options.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
}
