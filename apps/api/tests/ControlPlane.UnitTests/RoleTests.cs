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
}
