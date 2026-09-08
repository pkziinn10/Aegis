using System.Threading.RateLimiting;
using Aegis.Api.Configuration;
using Aegis.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Aegis.Application;
using Aegis.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerOptionsConfigurator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAegisApplication();
builder.Services.AddAegisInfrastructure(builder.Configuration);

builder.Services.AddAuthorization();

builder.Services.AddOptions<CorsOptions>()
    .BindConfiguration(CorsOptions.SectionName)
    .Validate(CorsOptions.IsValid,
        "Cors origins must be unique absolute HTTPS URIs without paths, queries, fragments or wildcards.")
    .ValidateOnStart();

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
    ?? throw new InvalidOperationException("Cors configuration is required.");
if (!CorsOptions.IsValid(corsOptions))
{
    throw new InvalidOperationException("Cors:AllowedOrigins configuration is invalid.");
}

var allowedHosts = builder.Configuration["AllowedHosts"];
if (!AllowedHostsOptions.IsValid(allowedHosts))
    throw new InvalidOperationException("AllowedHosts must contain unique explicit host names or IP addresses.");

builder.Services.AddOptions<ReverseProxyOptions>()
    .BindConfiguration(ReverseProxyOptions.SectionName)
    .Validate(ReverseProxyOptions.IsValid,
        "Enabled reverse proxy must contain unique valid known proxy IP addresses.")
    .ValidateOnStart();
var reverseProxyOptions = builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
    ?? new ReverseProxyOptions();
if (!ReverseProxyOptions.IsValid(reverseProxyOptions))
    throw new InvalidOperationException("ReverseProxy configuration is invalid.");

builder.Services.AddCors(options =>
    options.AddPolicy("Browser", policy =>
        policy.WithOrigins(corsOptions.AllowedOrigins)
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

    options.AddPolicy(SecurityPolicyNames.AuthByIp, context =>
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

if (reverseProxyOptions.Enabled)
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
        RequireHeaderSymmetry = true
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    foreach (var proxy in reverseProxyOptions.KnownProxies)
        forwardedHeaders.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    app.UseForwardedHeaders(forwardedHeaders);
}

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

public partial class Program;
