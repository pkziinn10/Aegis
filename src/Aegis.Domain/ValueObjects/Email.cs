using System.Text.RegularExpressions;
using Aegis.Domain.Results;

namespace Aegis.Domain.ValueObjects;

public sealed class Email : IEquatable<Email>
{
    private static readonly Regex Format = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Email(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsValid(normalized))
            throw new ArgumentException("E-mail inválido.", nameof(value));
        Value = normalized;
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsControl) || !Format.IsMatch(value)) return false;
        var parts = value.Split('@');
        var local = parts[0];
        var domain = parts[1];
        var labels = domain.Split('.');
        return local.Length <= 64 && domain.Length <= 255 && labels.All(IsDnsLabel) &&
            !local.StartsWith('.') && !local.EndsWith('.') && !local.Contains("..") &&
            !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..") &&
            !domain.StartsWith('-') && !domain.EndsWith('-');
    }

    private static bool IsDnsLabel(string label) => label.Length is > 0 and <= 63 &&
        label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
        label[0] != '-' && label[^1] != '-';

    public string Value { get; }

    public static DomainResult<Email> Create(string? value)
    {
        try { return DomainResult<Email>.Success(new Email(value ?? string.Empty)); }
        catch (ArgumentException) { return DomainResult<Email>.Failure(DomainErrorCode.InvalidEmail); }
    }

    public bool Equals(Email? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as Email);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
    public static bool operator ==(Email? left, Email? right) => Equals(left, right);
    public static bool operator !=(Email? left, Email? right) => !Equals(left, right);
}
