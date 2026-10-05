using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Roles;

/// <summary>Renames a custom role or changes its permissions; built-in roles cannot be changed (0030).</summary>
public sealed record ChangeRole(string ActorId, Guid RoleId, string Name, IReadOnlyCollection<string> Permissions) : IAuditedCommand
{
    object IAuditedCommand.AuditDetails => new { RoleId, Name, Permissions };
}

public static class ChangeRoleHandler
{
    public static async Task<Result<RoleDetails>> HandleAsync(ChangeRole command, ITenantCatalog catalog, CancellationToken cancellationToken)
    {
        var actor = await Actor.FindAsync(catalog, command.ActorId, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        if (await catalog.FindRoleAsync(command.RoleId, cancellationToken) is not { } role)
        {
            return Errors.RoleNotFound;
        }

        var before = role.Permissions;
        var changed = role.Change(command.Name, command.Permissions);
        if (!changed.IsSuccess)
        {
            return changed.Error;
        }

        // Both what the role held and what it will hold must be the actor's own: they can neither give nor take away more. A
        // refused change is not saved.
        if (RoleGrant.Allows(actor.Value.Permissions, [.. before, .. role.Permissions]) is { IsSuccess: false } refused)
        {
            return refused.Error;
        }

        if (await catalog.IsRoleNameTakenAsync(role.Name, exceptRoleId: role.Id, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        await catalog.SaveChangesAsync(cancellationToken);
        return role.ToDetails();
    }
}
