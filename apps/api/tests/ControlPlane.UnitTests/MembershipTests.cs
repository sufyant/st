using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;

namespace ControlPlane.UnitTests;

// A tenant always keeps at least one owner (0030).
public class MembershipTests
{
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly Guid UserId = new("0199a8f0-0000-7000-8000-000000000301");

    [Fact]
    public void The_last_owner_cannot_be_demoted()
    {
        var owner = new Membership(TenantId, UserId, BuiltInRoles.Owner.Id);

        var changed = owner.ChangeRole(BuiltInRoles.Admin, ownerCount: 1);

        changed.Error.Code.ShouldBe("membership.last_owner");
        owner.RoleId.ShouldBe(BuiltInRoles.Owner.Id);
    }

    [Fact]
    public void An_owner_can_be_demoted_while_another_owner_remains()
    {
        var owner = new Membership(TenantId, UserId, BuiltInRoles.Owner.Id);

        var changed = owner.ChangeRole(BuiltInRoles.Admin, ownerCount: 2);

        changed.IsSuccess.ShouldBeTrue();
        owner.RoleId.ShouldBe(BuiltInRoles.Admin.Id);
    }

    [Fact]
    public void The_last_owner_keeps_the_owner_role_when_it_is_assigned_again()
    {
        var owner = new Membership(TenantId, UserId, BuiltInRoles.Owner.Id);

        var changed = owner.ChangeRole(BuiltInRoles.Owner, ownerCount: 1);

        changed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_last_owner_cannot_be_removed()
    {
        var owner = new Membership(TenantId, UserId, BuiltInRoles.Owner.Id);

        var removable = owner.EnsureRemovable(ownerCount: 1);

        removable.Error.Code.ShouldBe("membership.last_owner");
    }

    [Fact]
    public void A_member_who_is_not_an_owner_can_be_removed_from_a_tenant_with_one_owner()
    {
        var member = new Membership(TenantId, UserId, BuiltInRoles.Member.Id);

        var removable = member.EnsureRemovable(ownerCount: 1);

        removable.IsSuccess.ShouldBeTrue();
    }
}
