using Aegis.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aegis.Api.Security;

public sealed class RedisRateLimitStore(IConnectionMultiplexer redis, string secret, IConfiguration configuration)
{
    private const string Script = "local n=redis.call('INCR',KEYS[1]); if n==1 then redis.call('EXPIRE',KEYS[1],ARGV[1]) end; return n";
    private readonly IDatabase database = redis.GetDatabase();
    private readonly byte[] secretBytes = Encoding.UTF8.GetBytes(secret);

    public async Task<bool> TryAcquireAsync(string scope, string identity, int limit, int windowSeconds, CancellationToken ct = default)
    {
        var digest = Convert.ToHexString(HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var prefix = configuration["RateLimiting:KeyPrefix"] ?? "aegis";
        var key = $"{prefix}:ratelimit:v1:{scope}:{digest}";
        try
        {
            var count = (long)await database.ScriptEvaluateAsync(Script, new RedisKey[] { key }, new RedisValue[] { windowSeconds });
            return count <= limit;
        }
        catch (RedisException) { return false; }
        catch (TimeoutException) { return false; }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    }
}

public sealed class AccountRateLimiter(RedisRateLimitStore store, IConfiguration configuration)
{
    public Task<bool> TryAcquireAsync(CredentialsRequest request, CancellationToken ct = default) =>
        store.TryAcquireAsync("account-login", request.Email.Trim().ToLowerInvariant(), PermitLimit, WindowSeconds, ct);

    private int PermitLimit => Math.Clamp(configuration.GetValue("RateLimiting:LoginAccountPermitLimit", 5), 1, 1000);
    private int WindowSeconds => Math.Clamp(configuration.GetValue("RateLimiting:WindowSeconds", 60), 1, 3600);
}

public sealed class AccountRateLimitFilter(AccountRateLimiter limiter) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.Values.OfType<CredentialsRequest>().FirstOrDefault() is { } request
            && !await limiter.TryAcquireAsync(request, context.HttpContext.RequestAborted))
        {
            await ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "RateLimitExceeded");
            return;
        }
        await next();
    }
}

public sealed class CredentialsRequestJsonConverter : JsonConverter<CredentialsRequest>
{
    public override CredentialsRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        string? email = null, password = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var name = reader.GetString()!;
            var known = name.Equals("email", StringComparison.OrdinalIgnoreCase) || name.Equals("password", StringComparison.OrdinalIgnoreCase);
            if (known && !seen.Add(name)) throw new JsonException();
            if (!reader.Read()) throw new JsonException();
            if (known)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                if (name.Equals("email", StringComparison.OrdinalIgnoreCase)) email = reader.GetString(); else password = reader.GetString();
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        }
        return new CredentialsRequest(email ?? "", password ?? "");
    }
    public override void Write(Utf8JsonWriter writer, CredentialsRequest value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new { value.Email, value.Password }, options);
}
