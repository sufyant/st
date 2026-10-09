using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.UnitTests;

public class RoleTests
{
    [Fact]
    public void No_built_in_role_holds_a_system_permission()
    {
        var systemPermissions = BuiltInRoles.All.SelectMany(role => role.Permissions).Intersect(Permissions.SystemPool);

        systemPermissions.ShouldBeEmpty();
    }

    [Fact]
    public void ReadMembers_EveryBuiltInRole_HoldsThePermission()
    {
        var roles = BuiltInRoles.All;

        roles.ShouldAllBe(role => role.Permissions.Contains("members.read"));
    }
}
