using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.UnitTests;

// No one can hand out a permission they do not hold, nor take a role away from someone who holds more than they do.
public class RoleGrantTests
{
    [Fact]
    public void An_owner_may_grant_the_owner_role()
    {
        var allowed = RoleGrant.Allows(BuiltInRoles.Owner.Permissions, BuiltInRoles.Owner.Permissions);

        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_admin_may_grant_the_admin_role()
    {
        var allowed = RoleGrant.Allows(BuiltInRoles.Admin.Permissions, BuiltInRoles.Admin.Permissions);

        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_admin_may_not_grant_the_owner_role()
    {
        var allowed = RoleGrant.Allows(BuiltInRoles.Admin.Permissions, BuiltInRoles.Owner.Permissions);

        allowed.Error.Code.ShouldBe("role.beyond_your_permissions");
        allowed.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public void An_admin_may_not_take_the_owner_role_away()
    {
        var allowed = RoleGrant.Allows(BuiltInRoles.Admin.Permissions, [.. BuiltInRoles.Owner.Permissions, .. BuiltInRoles.Viewer.Permissions]);

        allowed.Error.Code.ShouldBe("role.beyond_your_permissions");
    }

    [Fact]
    public void A_member_may_not_grant_a_permission_they_do_not_hold()
    {
        var allowed = RoleGrant.Allows(new HashSet<string> { Permissions.MembersInvite }, [Permissions.MembersInvite, Permissions.RolesManage]);

        allowed.Error.Code.ShouldBe("role.beyond_your_permissions");
    }
}
