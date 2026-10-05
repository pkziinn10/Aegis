using System.Text.Json.Serialization;
using Aegis.Domain.Results;

namespace Aegis.Domain.ValueObjects;

public sealed class PasswordHash : IEquatable<PasswordHash>
{
    private PasswordHash(string value) => Value = value;

    [JsonIgnore]
    public string Value { get; }

    public static DomainResult<PasswordHash> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DomainResult<PasswordHash>.Failure(DomainErrorCode.InvalidPasswordHash);

        return DomainResult<PasswordHash>.Success(new PasswordHash(value));
    }

    public bool Equals(PasswordHash? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as PasswordHash);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => "[REDACTED]";
}
