using System.Text.RegularExpressions;

namespace Api.Domain;

public sealed partial class TenantSlug : ValueObject
{
    private const int PostgresSchemaNameMaxLength = 63;

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public string Value { get; }

    private TenantSlug(string value)
    {
        Value = value;
    }

    public static TenantSlug Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant slug cannot be empty.", nameof(value));
        }

        if (value.Length > PostgresSchemaNameMaxLength)
        {
            throw new ArgumentException(
                $"Tenant slug cannot exceed {PostgresSchemaNameMaxLength} characters (Postgres schema name limit).",
                nameof(value));
        }

        if (!SlugPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid tenant slug (lowercase letters, digits, single hyphens only, no leading/trailing hyphen).",
                nameof(value));
        }

        return new TenantSlug(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
