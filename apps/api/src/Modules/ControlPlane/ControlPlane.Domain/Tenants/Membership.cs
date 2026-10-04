using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Domain.Tenants;

/// <summary>
/// A user's membership in a tenant, with the role it is assigned; authorization comes from here, not from the user record
/// (0029, 0030). A tenant always keeps at least one owner: the caller passes the tenant's current number of owners, read while
/// the tenant's memberships are locked against concurrent changes.
/// </summary>
internal sealed class Membership(Guid tenantId, Guid userId, Guid roleId)
{
    private static readonly Error LastOwner = Error.Conflict("membership.last_owner", "A tenant always keeps at least one owner.");

    public Guid TenantId { get; private init; } = tenantId;

    public Guid UserId { get; private init; } = userId;

    public Guid RoleId { get; private set; } = roleId;

    public bool IsOwner => RoleId == BuiltInRoles.Owner.Id;

    public Result ChangeRole(Role role, int ownerCount)
    {
        if (IsOwner && !role.IsOwner && ownerCount <= 1)
        {
            return LastOwner;
        }

        RoleId = role.Id;
        return Result.Success();
    }

    public Result EnsureRemovable(int ownerCount) => IsOwner && ownerCount <= 1 ? LastOwner : Result.Success();
}
