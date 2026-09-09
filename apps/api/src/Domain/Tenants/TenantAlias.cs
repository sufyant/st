using System.Text.RegularExpressions;

namespace Domain.Tenants;

public sealed record TenantAlias
{
    private static readonly HashSet<string> ReservedValues = new(StringComparer.Ordinal)
    {
        "admin",
        "api",
        "docs",
        "health",
        "hubs",
        "metrics",
        "openapi",
        "swagger",
        "system"
    };

    private static readonly Regex PathSegmentPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.CultureInvariant);

    public string Value { get; }

    private TenantAlias(string value)
    {
        Value = value;
    }

    public static TenantAlias Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !PathSegmentPattern.IsMatch(value) ||
            ReservedValues.Contains(value))
        {
            throw new ArgumentException("Tenant alias must be a 3-63 character lowercase URL path segment.", nameof(value));
        }

        return new TenantAlias(value);
    }
}
