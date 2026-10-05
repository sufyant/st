using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.UnitTests;

// Members and viewers hold the permissions of the capabilities modules add for them (0030).
public sealed class BuiltInRolesTests
{
    // A member schedules notifications for themselves (0027); a viewer only reads, so it holds no permission that writes.
    [Fact]
    public void A_member_may_schedule_notifications_and_a_viewer_may_not()
    {
        BuiltInRoles.Member.Permissions.ShouldBe([Permissions.NotificationsSchedule]);
        BuiltInRoles.Viewer.Permissions.ShouldBeEmpty();
    }
}
