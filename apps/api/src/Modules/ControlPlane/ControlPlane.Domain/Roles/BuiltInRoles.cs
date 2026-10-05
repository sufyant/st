using System.Collections.Frozen;
using SharedKernel;

namespace ControlPlane.Domain.Roles;

/// <summary>
/// The roles every tenant has (0030). Their permissions are fixed here and they cannot be changed or deleted. They are stored once,
/// with these ids, as catalog rows that belong to no tenant, so memberships and invitations reference every role the same way.
/// </summary>
internal static class BuiltInRoles
{
    public static Role Owner { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000001"), BuiltInRole.Owner);

    public static Role Admin { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000002"), BuiltInRole.Admin);

    public static Role Member { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000003"), BuiltInRole.Member);

    public static Role Viewer { get; } = Role.CreateBuiltIn(new("00000000-0000-7000-8000-000000000004"), BuiltInRole.Viewer);

    public static IReadOnlyList<Role> All { get; } = [Owner, Admin, Member, Viewer];

    private static readonly FrozenSet<string> OwnerPermissions = Permissions.TenantPool.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> AdminPermissions =
        OwnerPermissions.Except([Permissions.OwnersManage]).ToFrozenSet(StringComparer.Ordinal);

    // Members and viewers get the permissions of the capabilities that modules add for them. A viewer only reads, and no module
    // has a permission that only reads yet.
    private static readonly FrozenSet<string> MemberPermissions = FrozenSet.Create(StringComparer.Ordinal, Permissions.NotificationsSchedule);

    private static readonly FrozenSet<string> NoPermissions = FrozenSet<string>.Empty;

    public static IReadOnlySet<string> PermissionsOf(BuiltInRole role) => role switch
    {
        BuiltInRole.Owner => OwnerPermissions,
        BuiltInRole.Admin => AdminPermissions,
        BuiltInRole.Member => MemberPermissions,
        BuiltInRole.Viewer => NoPermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Every built-in role has its permissions."),
    };
}
