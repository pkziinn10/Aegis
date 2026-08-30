using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Aegis.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .Validate(JwtOptions.HasMinimumSecretLength,
        "JWT secret must contain at least 32 UTF-8 bytes.")
    .Validate(options => options.Algorithm == SecurityAlgorithms.HmacSha256,
        "Only HS256 is supported.")
    .Validate(options => options.Issuer == "Aegis.Api",
        "JWT issuer must be Aegis.Api.")
    .Validate(options => options.Audience == "Aegis.Client",
        "JWT audience must be Aegis.Client.")
    .Validate(options => options.KeyId == "aegis-primary-01",
        "JWT key id must be aegis-primary-01.")
    .Validate(options => options.AccessTokenExpirationMinutes is > 0 and <= 15,
        "Access token expiration must be between 1 and 15 minutes.")
    .Validate(options => options.RefreshTokenExpirationDays is > 0 and <= 7,
        "Refresh token expiration must be between 1 and 7 days.")
    .Validate(options => options.ClockSkewSeconds is >= 0 and <= 30,
        "Clock skew must be between 0 and 30 seconds.")
    .ValidateOnStart();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is required.");

var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwtOptions.SecretKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
                if (!string.Equals(
                        tokenKid,
                        jwtOptions.KeyId,
                        StringComparison.Ordinal))
                {
                    return [];
                }

                return [signingKey];
            }
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (string.IsNullOrWhiteSpace(
                        context.Principal?.FindFirst(
                            JwtRegisteredClaimNames.Sub)?.Value))
                {
                    context.Fail("The JWT must contain a subject claim.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

if (allowedOrigins is null
    || allowedOrigins.Length == 0
    || allowedOrigins.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins must contain at least one origin.");
}

builder.Services.AddCors(options =>
    options.AddPolicy("Browser", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowCredentials()
            .WithHeaders("Authorization", "Content-Type", "X-CSRF-TOKEN")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")));

builder.Services.AddCookiePolicy(options =>
{
    options.HttpOnly = HttpOnlyPolicy.Always;
    options.Secure = CookieSecurePolicy.Always;
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(30);
    options.IncludeSubDomains = true;
    options.Preload = false;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsync(
            "{\"title\":\"Too Many Requests\",\"status\":429}");
    };

    options.AddPolicy("AuthByIp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("Browser");
app.UseCookiePolicy();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
