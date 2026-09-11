using System.Text;
using System.Text.RegularExpressions;

namespace Domain.ControlPlane.Tenants;

public sealed record TenantDatabaseName
{
    private const int MaximumByteLength = 63;

    private static readonly HashSet<string> ReservedValues = new(StringComparer.Ordinal)
    {
        "postgres",
        "template0",
        "template1",
        "control_plane"
    };

    private static readonly Regex IdentifierPattern = new(
        "\\A[a-z][a-z0-9_]*\\z",
        RegexOptions.CultureInvariant);

    public string Value { get; }

    private TenantDatabaseName(string value)
    {
        Value = value;
    }

    public static TenantDatabaseName ForTenant(TenantId tenantId) =>
        new($"tenant_{tenantId.Value:N}");

    public static TenantDatabaseName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !IdentifierPattern.IsMatch(value) ||
            Encoding.UTF8.GetByteCount(value) > MaximumByteLength ||
            ReservedValues.Contains(value))
        {
            throw new ArgumentException(
                "Tenant database name must be a lowercase PostgreSQL identifier of at most 63 bytes.",
                nameof(value));
        }

        return new TenantDatabaseName(value);
    }
}
