using System.Collections.Frozen;
using SharedKernel;

namespace ControlPlane.Domain.Roles;

/// <summary>
/// The roles every tenant has. Their permissions are fixed here and they cannot be changed or deleted. They are stored once,
/// with these ids, as catalog rows that belong to no tenant, so memberships and invitations reference every role the same way.
/// </summary>
internal static class BuiltInRoles
{
    public static Role Owner { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000001"), BuiltInRole.Owner);

    public static Role Admin { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000002"), BuiltInRole.Admin);

    public static Role Member { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000003"), BuiltInRole.Member);

    public static IReadOnlyList<Role> All { get; } = [Owner, Admin, Member];

    private static readonly FrozenSet<string> OwnerPermissions = Permissions.TenantPool.ToFrozenSet(StringComparer.Ordinal);

    // Admins hold what owners hold: the tenant pool has no permission that is the owners' alone.
    private static readonly FrozenSet<string> AdminPermissions = OwnerPermissions;

    // Members get the permissions of the capabilities that modules add for them; no module has one yet.
    private static readonly FrozenSet<string> MemberPermissions = FrozenSet<string>.Empty;

    public static IReadOnlySet<string> PermissionsOf(BuiltInRole role) => role switch
    {
        BuiltInRole.Owner => OwnerPermissions,
        BuiltInRole.Admin => AdminPermissions,
        BuiltInRole.Member => MemberPermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Every built-in role has its permissions."),
    };
}
