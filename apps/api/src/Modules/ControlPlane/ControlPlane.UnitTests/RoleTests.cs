using ControlPlane.Domain;
using ControlPlane.Domain.Roles;

namespace ControlPlane.UnitTests;

public class RoleTests
{
    [Fact]
    public void No_built_in_role_holds_a_system_permission()
    {
        var systemPermissions = BuiltInRoles.All.SelectMany(role => role.Permissions).Intersect(PermissionPools.SystemPool);

        systemPermissions.ShouldBeEmpty();
    }

    [Fact]
    public void ReadMembers_EveryBuiltInRole_HoldsThePermission()
    {
        var roles = BuiltInRoles.All;

        roles.ShouldAllBe(role => role.Permissions.Contains("members.read"));
    }
}
