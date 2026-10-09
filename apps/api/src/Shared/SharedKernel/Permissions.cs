using System.Collections.Frozen;

namespace SharedKernel;

/// <summary>
/// The permission catalogue. Permissions are fixed in code, each tied to a capability. Tenant roles choose from the tenant
/// pool only; system admins hold the system pool. Endpoints name a permission as their authorization policy.
/// </summary>
public static class Permissions
{
    public const string SystemTenantsCreate = "system.tenants.create";

    public const string MembersRead = "members.read";

    public static IReadOnlySet<string> TenantPool { get; } = FrozenSet.Create(StringComparer.Ordinal, MembersRead);

    public static IReadOnlySet<string> SystemPool { get; } = FrozenSet.Create(StringComparer.Ordinal, SystemTenantsCreate);
}
