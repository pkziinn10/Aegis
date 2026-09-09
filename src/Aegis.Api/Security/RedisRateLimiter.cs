using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Aegis.Api.Security;

public sealed class RedisRateLimiterPolicy(RedisRateLimitStore store, IHttpContextAccessor accessor, int limit, int windowSeconds, string scope, Func<HttpContext, string> identity)
    : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
    public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
        RateLimitPartition.Get<string>(scope, _ => new RedisRateLimiter(store, accessor, limit, windowSeconds, scope, identity));
}

internal sealed class RedisRateLimiter(RedisRateLimitStore store, IHttpContextAccessor accessor, int limit, int windowSeconds, string scope, Func<HttpContext, string> identity) : RateLimiter
{
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        try
        {
            var allowed = permitCount == 1 && accessor.HttpContext is { } context && store.TryAcquireAsync(scope, identity(context), limit, windowSeconds).GetAwaiter().GetResult();
            return new RedisLease(allowed, allowed ? TimeSpan.Zero : TimeSpan.FromSeconds(windowSeconds));
        }
        catch { return new RedisLease(false, TimeSpan.Zero); }
    }
    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        => AcquireCoreAsync(permitCount, cancellationToken);
    private async ValueTask<RateLimitLease> AcquireCoreAsync(int permitCount, CancellationToken ct)
    {
        var allowed = permitCount == 1 && accessor.HttpContext is { } context && await store.TryAcquireAsync(scope, identity(context), limit, windowSeconds, ct);
        return new RedisLease(allowed, allowed ? TimeSpan.Zero : TimeSpan.FromSeconds(windowSeconds));
    }
    public override TimeSpan? IdleDuration => null;
    public override RateLimiterStatistics? GetStatistics() => null;
    protected override void Dispose(bool disposing) { }
}

internal sealed class RedisLease(bool acquired, TimeSpan retryAfter) : RateLimitLease
{
    public override bool IsAcquired => acquired;
    public override IEnumerable<string> MetadataNames => retryAfter > TimeSpan.Zero ? [MetadataName.RetryAfter.Name] : [];
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (metadataName == MetadataName.RetryAfter.Name && retryAfter > TimeSpan.Zero) { metadata = retryAfter; return true; }
        metadata = null; return false;
    }
}
