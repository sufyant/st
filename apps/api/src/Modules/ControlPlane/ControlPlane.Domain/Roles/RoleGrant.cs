using SharedKernel;

namespace ControlPlane.Domain.Roles;

/// <summary>
/// Granting, changing or taking away a role, and shaping a custom role, is allowed only when every permission involved is the
/// actor's own: no one hands out a permission they do not hold, or takes a role from someone who holds more than they do (0030).
/// </summary>
internal static class RoleGrant
{
    public static Result Allows(IReadOnlySet<string> actorPermissions, IEnumerable<string> involvedPermissions) =>
        involvedPermissions.All(actorPermissions.Contains)
            ? Result.Success()
            : Error.Forbidden("role.beyond_your_permissions", "The role involves permissions you do not hold.");
}
