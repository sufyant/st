using ControlPlane.Domain.SystemAdmins;
using SharedKernel;

namespace ControlPlane.UnitTests;

public class SystemAdminTests
{
    [Fact]
    public void An_administrator_holds_the_system_permissions_and_no_tenant_permission()
    {
        var permissions = SystemRoles.PermissionsOf(SystemRole.Administrator);

        permissions.ShouldBe(Permissions.SystemPool, ignoreOrder: true);
    }
}
