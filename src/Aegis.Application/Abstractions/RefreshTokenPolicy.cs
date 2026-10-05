namespace Aegis.Application.Abstractions;

public sealed class RefreshTokenPolicy : IRefreshTokenPolicy
{
    public RefreshTokenPolicy(int expirationDays)
    {
        if (expirationDays is <= 0 or > 7) throw new ArgumentOutOfRangeException(nameof(expirationDays));
        ExpirationDays = expirationDays;
    }

    public int ExpirationDays { get; }
    public DateTimeOffset GetSessionExpiration(DateTimeOffset createdAt) => createdAt.AddDays(ExpirationDays);
    public DateTimeOffset GetRotationExpiration(DateTimeOffset now, DateTimeOffset sessionExpiration) =>
        now.AddDays(ExpirationDays) < sessionExpiration ? now.AddDays(ExpirationDays) : sessionExpiration;
}
