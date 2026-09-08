using System.Threading.RateLimiting;
using Aegis.Api;
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

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = context =>
        ApiErrors.From(context.HttpContext, Aegis.Application.Results.ApplicationErrorCode.InvalidRequest));
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-csrf-token";
    options.Cookie.HttpOnly = false;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerOptionsConfigurator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAegisApplication();
builder.Services.AddAegisInfrastructure(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SecurityPolicyNames.AuthenticatedUser, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(SecurityPolicyNames.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
});
builder.Services.AddSingleton<OriginValidator>();

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
        await ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "RateLimitExceeded");
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

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is BadHttpRequestException { StatusCode: StatusCodes.Status413RequestEntityTooLarge }
        || error is InvalidDataException)
    {
        await ApiErrors.WriteAsync(context, StatusCodes.Status413RequestEntityTooLarge, "PayloadTooLarge");
        return;
    }

    await ApiErrors.WriteAsync(context, StatusCodes.Status500InternalServerError, "InternalServerError");
}));

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
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var maxPayloadBytes = path is "/auth/browser/register" or "/auth/browser/login" or "/auth/token/register" or "/auth/token/login"
        ? 16 * 1024
        : path is "/auth/browser/refresh" or "/auth/token/refresh" or "/auth/token/logout" or "/auth/browser/logout"
            ? 8 * 1024
            : (int?)null;

    if (context.Request.Method == HttpMethods.Post && maxPayloadBytes is int limit)
    {
        if (context.Request.ContentLength > limit)
        {
            await ApiErrors.WriteAsync(context, StatusCodes.Status413RequestEntityTooLarge, "PayloadTooLarge");
            return;
        }

        context.Request.EnableBuffering();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer);
            total += read;
            if (total > limit)
            {
                await ApiErrors.WriteAsync(context, StatusCodes.Status413RequestEntityTooLarge, "PayloadTooLarge");
                return;
            }

            if (read == 0) break;
        }

        context.Request.Body.Position = 0;
    }

    await next();
});
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    const int maxBearerBytes = 8 * 1024;
    if (context.Request.Headers.Authorization.Count > 0
        && System.Text.Encoding.UTF8.GetByteCount(context.Request.Headers.Authorization.ToString()) > maxBearerBytes)
    {
        await ApiErrors.WriteAsync(context, StatusCodes.Status401Unauthorized, "InvalidBearerToken");
        return;
    }

    await next();
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    await next();
    if (!context.Response.HasStarted && context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        await ApiErrors.WriteAsync(context, context.Response.StatusCode, context.Response.StatusCode == 401 ? "Unauthorized" : "Forbidden");
});
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
