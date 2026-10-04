using System.Collections.Frozen;

namespace SharedKernel;

/// <summary>
/// The permission catalogue (0030). Permissions are fixed in code, each tied to a capability. Tenant roles choose from the tenant
/// pool only; system admins hold the system pool (0031). Endpoints name a permission as their authorization policy.
/// </summary>
public static class Permissions
{
    public const string MembersInvite = "members.invite";

    public const string MembersManage = "members.manage";

    public const string OwnersManage = "owners.manage";

    public const string RolesManage = "roles.manage";

    public const string SystemTenantsRead = "system.tenants.read";

    public const string SystemTenantsEnter = "system.tenants.enter";

    public const string SystemMembersInvite = "system.members.invite";

    public static IReadOnlySet<string> TenantPool { get; } =
        FrozenSet.Create(StringComparer.Ordinal, MembersInvite, MembersManage, OwnersManage, RolesManage);

    public static IReadOnlySet<string> SystemPool { get; } =
        FrozenSet.Create(StringComparer.Ordinal, SystemTenantsRead, SystemTenantsEnter, SystemMembersInvite);
}
