using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Roles;

/// <summary>Deletes a custom role that no member or pending invitation is assigned (0030).</summary>
public sealed record DeleteRole(string ActorId, Guid RoleId);

public static class DeleteRoleHandler
{
    public static async Task<Result> HandleAsync(DeleteRole command, ITenantCatalog catalog, CancellationToken cancellationToken)
    {
        // Locked so that no one is assigned the role between the check below and its deletion.
        await catalog.LockAsync(cancellationToken);

        var actor = await Actor.FindAsync(catalog, command.ActorId, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        if (await catalog.FindRoleAsync(command.RoleId, cancellationToken) is not { } role)
        {
            return Errors.RoleNotFound;
        }

        var deletable = role.EnsureDeletable();
        if (!deletable.IsSuccess)
        {
            return deletable;
        }

        if (RoleGrant.Allows(actor.Value.Permissions, role.Permissions) is { IsSuccess: false } refused)
        {
            return refused;
        }

        if (await catalog.IsRoleInUseAsync(role.Id, cancellationToken))
        {
            return Error.Conflict("role.in_use", "The role is assigned to a member or a pending invitation.");
        }

        catalog.Remove(role);
        await catalog.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
