using Aegis.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.Extensions.Options;

namespace Aegis.Api.Configuration;

public static class AuthenticationConfiguration
{
    public static IServiceCollection AddAegisAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerOptionsConfigurator>();
        return services;
    }

    public static IServiceCollection AddAegisCookiePolicy(this IServiceCollection services)
    {
        services.AddCookiePolicy(options =>
        {
            options.HttpOnly = HttpOnlyPolicy.Always;
            options.Secure = CookieSecurePolicy.Always;
            options.MinimumSameSitePolicy = SameSiteMode.Strict;
        });
        return services;
    }
}
