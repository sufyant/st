using System.Text.RegularExpressions;

namespace Api.Domain;

public sealed partial class Email : ValueObject
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        var trimmed = value.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(value));
        }

        if (!EmailPattern().IsMatch(trimmed))
        {
            throw new ArgumentException($"'{value}' is not a valid email address.", nameof(value));
        }

        return new Email(trimmed.ToLowerInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
