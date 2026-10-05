using System.Text.Json.Serialization;
using Aegis.Domain.Results;

namespace Aegis.Domain.ValueObjects;

public sealed class Password : IEquatable<Password>
{
    public const int MinimumLength = 12;

    private Password(string value) => Value = value;

    [JsonIgnore]
    public string Value { get; }

    public static DomainResult<Password> Create(string? value)
    {
        if (value is null || value.Length < MinimumLength)
            return DomainResult<Password>.Failure(DomainErrorCode.InvalidPassword);

        return DomainResult<Password>.Success(new Password(value));
    }

    public bool Equals(Password? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as Password);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => "[REDACTED]";
}
