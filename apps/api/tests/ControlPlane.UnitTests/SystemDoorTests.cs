using ControlPlane.Domain;
using ControlPlane.Domain.SystemAdmins;

namespace ControlPlane.UnitTests;

// A6: the system door needs a system admin with a second factor verified in the session.
public class SystemDoorTests
{
    [Fact]
    public void EnterSystemDoor_SystemAdminWithASecondFactor_HoldsTheSystemPool()
    {
        var permissions = SystemDoor.PermissionsFor(isSystemAdmin: true, secondFactorVerified: true);

        permissions.ShouldBe(PermissionPools.SystemPool);
    }

    [Fact]
    public void EnterSystemDoor_SystemAdminWithoutASecondFactor_IsRefused()
    {
        var permissions = SystemDoor.PermissionsFor(isSystemAdmin: true, secondFactorVerified: false);

        permissions.ShouldBeNull();
    }

    [Fact]
    public void EnterSystemDoor_UserWhoIsNotASystemAdmin_IsRefused()
    {
        var permissions = SystemDoor.PermissionsFor(isSystemAdmin: false, secondFactorVerified: true);

        permissions.ShouldBeNull();
    }
}
