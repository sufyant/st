using System.Collections.Frozen;

namespace SharedKernel;

/// <summary>
/// The permission catalogue (0030). Permissions are fixed in code, each tied to a capability. Tenant roles choose from the tenant
/// pool only; system admins hold the system pool (0031). Endpoints name a permission as their authorization policy.
/// </summary>
public static class Permissions
{
    public const string SystemTenantsCreate = "system.tenants.create";

    // No tenant capability has a permission yet.
    public static IReadOnlySet<string> TenantPool { get; } = FrozenSet<string>.Empty;

    public static IReadOnlySet<string> SystemPool { get; } = FrozenSet.Create(StringComparer.Ordinal, SystemTenantsCreate);
}
