using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.UnitTests;

public class RoleTests
{
    private static readonly Guid Id = new("0199a8f0-0000-7000-8000-000000000101");
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");

    [Fact]
    public void An_owner_holds_every_tenant_permission()
    {
        BuiltInRoles.Owner.Permissions.ShouldBe(Permissions.TenantPool, ignoreOrder: true);
    }

    [Fact]
    public void An_admin_holds_every_tenant_permission_except_managing_owners()
    {
        BuiltInRoles.Admin.Permissions.ShouldBe(
            [Permissions.MembersInvite, Permissions.MembersManage, Permissions.RolesManage], ignoreOrder: true);
    }

    [Fact]
    public void No_built_in_role_holds_a_system_permission()
    {
        var systemPermissions = BuiltInRoles.All.SelectMany(role => role.Permissions).Intersect(Permissions.SystemPool);

        systemPermissions.ShouldBeEmpty();
    }

    [Fact]
    public void A_custom_role_is_created_with_permissions_from_the_tenant_pool()
    {
        var role = Role.CreateCustom(Id, TenantId, "Recruiter", [Permissions.MembersInvite]);

        role.Value.Permissions.ShouldBe([Permissions.MembersInvite]);
        role.Value.TenantId.ShouldBe(TenantId);
    }

    [Theory]
    [InlineData(Permissions.SystemTenantsEnter)]
    [InlineData(Permissions.SystemTenantsRead)]
    [InlineData("billing.refund")]
    public void A_custom_role_cannot_hold_a_permission_outside_the_tenant_pool(string permission)
    {
        var role = Role.CreateCustom(Id, TenantId, "Escalated", [Permissions.MembersInvite, permission]);

        role.Error.Code.ShouldBe("role.permission_not_allowed");
    }

    [Fact]
    public void A_custom_role_cannot_be_changed_to_hold_a_system_permission()
    {
        var role = Role.CreateCustom(Id, TenantId, "Recruiter", [Permissions.MembersInvite]).Value;

        var changed = role.Change("Recruiter", [Permissions.SystemTenantsEnter]);

        changed.Error.Code.ShouldBe("role.permission_not_allowed");
        role.Permissions.ShouldBe([Permissions.MembersInvite]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner")]
    [InlineData("Viewer")]
    public void A_custom_role_needs_a_name_of_its_own(string name)
    {
        var role = Role.CreateCustom(Id, TenantId, name, []);

        role.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void A_built_in_role_cannot_be_changed()
    {
        var changed = BuiltInRoles.Viewer.Change("Viewer", [Permissions.MembersInvite]);

        changed.Error.Code.ShouldBe("role.built_in");
        BuiltInRoles.Viewer.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public void A_built_in_role_cannot_be_deleted()
    {
        var deletable = BuiltInRoles.Member.EnsureDeletable();

        deletable.Error.Code.ShouldBe("role.built_in");
    }

    [Fact]
    public void A_custom_role_can_be_deleted()
    {
        var role = Role.CreateCustom(Id, TenantId, "Recruiter", []).Value;

        role.EnsureDeletable().IsSuccess.ShouldBeTrue();
    }
}
