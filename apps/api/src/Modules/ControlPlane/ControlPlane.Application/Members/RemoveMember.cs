using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Members;

/// <summary>Removes a member from the tenant; the tenant keeps at least one owner (0030).</summary>
public sealed record RemoveMember(string ActorId, Guid UserId) : IAuditedCommand
{
    object IAuditedCommand.AuditDetails => new { UserId };
}

public static class RemoveMemberHandler
{
    public static async Task<Result> HandleAsync(RemoveMember command, ITenantCatalog catalog, CancellationToken cancellationToken)
    {
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

        var role = await catalog.FindRoleAsync(membership.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Every membership has a role.");
        if (RoleGrant.Allows(actor.Value.Permissions, role.Permissions) is { IsSuccess: false } refused)
        {
            return refused;
        }

        var removable = membership.EnsureRemovable(await catalog.CountOwnersAsync(cancellationToken));
        if (!removable.IsSuccess)
        {
            return removable;
        }

        catalog.Remove(membership);
        await catalog.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
