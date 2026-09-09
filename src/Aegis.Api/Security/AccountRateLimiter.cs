using Aegis.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aegis.Api.Security;

public sealed class AccountRateLimiter(IConfiguration configuration, string accountKeySecret)
{
    private readonly string secret = accountKeySecret;
    private readonly object gate = new();
    private readonly Dictionary<string, AccountPartition> partitions = new(StringComparer.Ordinal);

    public bool TryAcquire(CredentialsRequest request)
    {
        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var key = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(email)));
            var now = DateTimeOffset.UtcNow;
            lock (gate)
            {
                foreach (var expired in partitions
                    .Where(pair => pair.Value.ExpiresAt <= now)
                    .Select(pair => pair.Key)
                    .ToArray())
                {
                    partitions.Remove(expired);
                }

                if (!partitions.TryGetValue(key, out var partition))
                {
                    if (partitions.Count >= PartitionCapacity)
                        return false;

                    partition = new AccountPartition(PermitLimit, now + TimeSpan.FromSeconds(WindowSeconds));
                    partitions.Add(key, partition);
                }

                return partition.TryAcquire();
            }
        }
        catch (InvalidOperationException) { return false; }
    }

    private int PermitLimit => Math.Clamp(configuration.GetValue("RateLimiting:LoginAccountPermitLimit", 5), 1, 1000);
    private int WindowSeconds => Math.Clamp(configuration.GetValue("RateLimiting:WindowSeconds", 60), 1, 3600);
    private int PartitionCapacity => Math.Clamp(configuration.GetValue("RateLimiting:AccountPartitionCapacity", 10_000), 1, 100_000);
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
            var known = name.Equals("email", StringComparison.OrdinalIgnoreCase)
                || name.Equals("password", StringComparison.OrdinalIgnoreCase);
            if (known && !seen.Add(name)) throw new JsonException();
            if (!reader.Read()) throw new JsonException();
            if (known)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                var value = reader.GetString();
                if (name.Equals("email", StringComparison.OrdinalIgnoreCase)) email = value;
                else password = value;
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
        return new CredentialsRequest(email ?? "", password ?? "");
    }

    public override void Write(Utf8JsonWriter writer, CredentialsRequest value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new { value.Email, value.Password }, options);
}

public sealed class AccountRateLimitFilter(AccountRateLimiter limiter) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.Values.OfType<CredentialsRequest>().FirstOrDefault() is { } request
            && !limiter.TryAcquire(request))
        {
            await ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "RateLimitExceeded");
            return;
        }

        await next();
    }
}

internal sealed class AccountPartition(int permitLimit, DateTimeOffset expiresAt)
{
    private readonly int permitLimit = permitLimit;
    private int count;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public bool TryAcquire()
    {
        return count < permitLimit && ++count > 0;
    }
}
