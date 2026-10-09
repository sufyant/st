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

    // Section 6: while the staff list is empty, the person with the configured email address verified and a second factor becomes
    // the first system admin.
    [Fact]
    public void BecomeFirstSystemAdmin_TheConfiguredEmailIsVerifiedWithASecondFactor_Qualifies()
    {
        var qualifies = SystemDoor.IsFirstSystemAdmin("Admin@Example.com", secondFactorVerified: true, ["other@example.com", "admin@example.com"]);

        qualifies.ShouldBeTrue();
    }

    [Fact]
    public void BecomeFirstSystemAdmin_WithoutASecondFactor_DoesNotQualify()
    {
        var qualifies = SystemDoor.IsFirstSystemAdmin("admin@example.com", secondFactorVerified: false, ["admin@example.com"]);

        qualifies.ShouldBeFalse();
    }

    [Fact]
    public void BecomeFirstSystemAdmin_AnotherEmailIsVerified_DoesNotQualify()
    {
        var qualifies = SystemDoor.IsFirstSystemAdmin("admin@example.com", secondFactorVerified: true, ["admin@example.org"]);

        qualifies.ShouldBeFalse();
    }
}
