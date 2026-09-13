using System.Text;
using System.Text.RegularExpressions;

namespace Domain.ControlPlane.Tenants;

public sealed record TenantRoleName
{
    private const int MaximumByteLength = 63;

    private static readonly Regex IdentifierPattern = new(
        "\\A[a-z][a-z0-9_]*\\z",
        RegexOptions.CultureInvariant);

    public string Value { get; }

    private TenantRoleName(string value)
    {
        Value = value;
    }

    public static TenantRoleName ForTenant(TenantId tenantId) =>
        new($"access_{tenantId.Value:N}");

    public static TenantRoleName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !IdentifierPattern.IsMatch(value) ||
            Encoding.UTF8.GetByteCount(value) > MaximumByteLength)
        {
            throw new ArgumentException(
                "Tenant role name must be a lowercase PostgreSQL identifier of at most 63 bytes.",
                nameof(value));
        }

        return new TenantRoleName(value);
    }
}
