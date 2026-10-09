using System.Collections.Frozen;

namespace ControlPlane.Domain;

/// <summary>
/// The permissions of the catalogue in their two pools. Tenant roles choose from the tenant pool only; system admins hold the
/// system pool (A4). Domain does not reference Contracts (section 2), so the names are stated here as well; a unit test keeps
/// them the same as the catalogue in <c>ControlPlane.Contracts</c>.
/// </summary>
internal static class PermissionPools
{
    public const string SystemTenantsCreate = "system.tenants.create";

    public const string MembersRead = "members.read";

    public static IReadOnlySet<string> TenantPool { get; } = FrozenSet.Create(StringComparer.Ordinal, MembersRead);

    public static IReadOnlySet<string> SystemPool { get; } = FrozenSet.Create(StringComparer.Ordinal, SystemTenantsCreate);
}
