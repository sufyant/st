using System.Text.RegularExpressions;

namespace Api.Domain;

public sealed partial class TenantSlug : ValueObject
{
    // Postgres identifiers are truncated at 63 bytes, and Tenant.SchemaName prepends
    // a "tenant_" (7-char) prefix to the slug, so the slug itself must leave room for
    // that prefix to avoid two different tenants silently colliding onto the same
    // truncated schema name.
    private const int PostgresSchemaNameMaxLength = 63 - 7; // 63 - "tenant_".Length

    private static readonly HashSet<string> ReservedSlugs = ["admin", "public"];

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
                $"Tenant slug cannot exceed {PostgresSchemaNameMaxLength} characters " +
                "(Postgres's 63-byte identifier limit minus the 7-character \"tenant_\" schema-name prefix).",
                nameof(value));
        }

        if (!SlugPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid tenant slug (lowercase letters, digits, single hyphens only, no leading/trailing hyphen).",
                nameof(value));
        }

        if (ReservedSlugs.Contains(value))
        {
            throw new ArgumentException($"'{value}' is a reserved tenant slug.", nameof(value));
        }

        return new TenantSlug(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
