using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Aegis.Infrastructure.Services;
using InfrastructureJwtOptions = Aegis.Infrastructure.Services.JwtOptions;
using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;

namespace Aegis.Api.Configuration;

public sealed class JwtBearerOptionsConfigurator(IOptions<InfrastructureJwtOptions> jwtOptionsAccessor)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is not null && name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwtOptions = jwtOptionsAccessor.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "roles",
            IssuerSigningKeyResolver = (_, _, tokenKid, _) =>
            {
                return jwtOptions.Keys
                    .Where(x => string.Equals(x.Kid, tokenKid, StringComparison.Ordinal))
                    .Select(x => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(x.Secret)));
            }
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (string.IsNullOrWhiteSpace(
                        context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value))
                {
                    context.Fail("The JWT must contain a subject claim.");
                }

                var iat = context.Principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
                var exp = context.Principal?.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
                var nbf = context.Principal?.FindFirst(JwtRegisteredClaimNames.Nbf)?.Value;
                if (!HasJsonNumericDate(context.SecurityToken, JwtRegisteredClaimNames.Nbf)
                    || !InfrastructureJwtOptions.TryReadNumericDate(iat, out var issuedAt)
                    || !InfrastructureJwtOptions.TryReadNumericDate(nbf, out var notBefore)
                    || !InfrastructureJwtOptions.TryReadNumericDate(exp, out var expiresAt))
                {
                    context.Fail("The JWT must contain valid iat, nbf and exp claims.");
                }
                else
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var maxDuration = TimeSpan.FromMinutes(jwtOptions.AccessTokenExpirationMinutes).TotalSeconds;
                    if (issuedAt > now + jwtOptions.ClockSkewSeconds
                        || notBefore > now + jwtOptions.ClockSkewSeconds
                        || issuedAt > expiresAt
                        || notBefore > expiresAt
                        || expiresAt - issuedAt > maxDuration)
                    {
                        context.Fail("The JWT issued-at and expiration window is invalid.");
                    }
                }

                return Task.CompletedTask;
            }
        };
    }

    private static bool HasJsonNumericDate(SecurityToken token, string claimName)
    {
        try
        {
            var encodedPayload = token switch
            {
                JwtSecurityToken jwt => jwt.RawData.Split('.')[1],
                JsonWebToken jsonWebToken => jsonWebToken.EncodedPayload,
                _ => null
            };

            if (encodedPayload is null) return false;

            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(encodedPayload));
            return payload.RootElement.TryGetProperty(claimName, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var seconds)
                && seconds >= 0;
        }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
        catch (IndexOutOfRangeException) { return false; }
    }

    public void Configure(JwtBearerOptions options) => Configure(
        JwtBearerDefaults.AuthenticationScheme,
        options);
}
