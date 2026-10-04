using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Members;

/// <summary>Assigns a member another role; the tenant keeps at least one owner (0030).</summary>
public sealed record ChangeMemberRole(string ActorId, Guid UserId, Guid RoleId);

public static class ChangeMemberRoleHandler
{
    public static async Task<Result> HandleAsync(ChangeMemberRole command, ITenantCatalog catalog, CancellationToken cancellationToken)
    {
        // Locked first, so the owner count below holds until the change commits; two owners cannot demote each other at once.
        await catalog.LockAsync(cancellationToken);

        var actor = await Actor.FindAsync(catalog, command.ActorId, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        if (await catalog.FindMembershipAsync(command.UserId, cancellationToken) is not { } membership)
        {
            return Errors.MembershipNotFound;
        }

        if (await catalog.FindRoleAsync(command.RoleId, cancellationToken) is not { } role)
        {
            return Errors.RoleNotFound;
        }

        var current = await catalog.FindRoleAsync(membership.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Every membership has a role.");
        if (RoleGrant.Allows(actor.Value.Permissions, [.. current.Permissions, .. role.Permissions]) is { IsSuccess: false } refused)
        {
            return refused;
        }

        var changed = membership.ChangeRole(role, await catalog.CountOwnersAsync(cancellationToken));
        if (!changed.IsSuccess)
        {
            return changed;
        }

        await catalog.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
