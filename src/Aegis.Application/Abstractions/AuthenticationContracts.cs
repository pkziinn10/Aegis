using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using System.Text.Json.Serialization;

namespace Aegis.Application.Abstractions;

public interface IPasswordHasher
{
    string DummyHash { get; }
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid userId, UserRole role, DateTimeOffset issuedAt);
}

public interface IRefreshTokenFactory
{
    RefreshTokenMaterial Create(Guid sessionId, DateTimeOffset createdAt, DateTimeOffset expiresAt);
    string Hash(RefreshTokenValue token);
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<TransactionOutcome<T>>> operation, CancellationToken cancellationToken = default);
}
public enum TransactionDecision { Commit, Rollback }
public sealed record TransactionOutcome<T>(T Result, TransactionDecision Decision);
public interface ICurrentUserContext { Guid? UserId { get; } }
public interface IAuditWriter
{
    Task WriteAsync(string action, Guid? userId, IReadOnlyDictionary<string, string?>? metadata = null, CancellationToken cancellationToken = default);
}

public sealed class AccessToken
{
    public AccessToken(string value, string type, DateTimeOffset expiresAt) => (Value, Type, ExpiresAt) = (value, type, expiresAt);
    [JsonIgnore] public string Value { get; }
    public string Type { get; }
    public DateTimeOffset ExpiresAt { get; }
    public override string ToString() => "[REDACTED ACCESS TOKEN]";
}

public sealed class RefreshTokenValue
{
    public RefreshTokenValue(string value, DateTimeOffset expiresAt) => (Value, ExpiresAt) = (value, expiresAt);
    [JsonIgnore] public string Value { get; }
    public DateTimeOffset ExpiresAt { get; }
    public override string ToString() => "[REDACTED REFRESH TOKEN]";
}

public sealed class RefreshTokenMaterial
{
    public RefreshTokenMaterial(RefreshTokenValue value, string hash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Hash inválido.", nameof(hash));
        if (expiresAt != value.ExpiresAt) throw new ArgumentException("Expirações inconsistentes.", nameof(expiresAt));
        if (expiresAt <= createdAt) throw new ArgumentException("Expiração inválida.", nameof(expiresAt));
        (Value, Hash, ExpiresAt) = (value, hash, expiresAt);
    }
    [JsonIgnore] public RefreshTokenValue Value { get; }
    [JsonIgnore] public string Hash { get; }
    public DateTimeOffset ExpiresAt { get; }
    public override string ToString() => "[REDACTED REFRESH TOKEN MATERIAL]";
}
