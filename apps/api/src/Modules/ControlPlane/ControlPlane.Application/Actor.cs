using ControlPlane.Application.Ports;
using SharedKernel;

namespace ControlPlane.Application;

// The member who sends a command, and the permissions their role holds in the active tenant.
internal sealed record Actor(Guid UserId, IReadOnlySet<string> Permissions)
{
    public static async Task<Result<Actor>> FindAsync(ITenantCatalog catalog, string externalUserId, CancellationToken cancellationToken)
    {
        if (await catalog.FindMembershipAsync(externalUserId, cancellationToken) is not { } membership)
        {
            return Error.Forbidden("actor.not_member", "Only a member of the tenant can do this.");
        }

        var role = await catalog.FindRoleAsync(membership.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Every membership has a role.");
        return new Actor(membership.UserId, role.Permissions);
    }
}
