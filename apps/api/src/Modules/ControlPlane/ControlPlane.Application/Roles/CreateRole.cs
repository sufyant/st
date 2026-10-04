using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Roles;

/// <summary>Creates a custom role in the active tenant from the tenant permission pool (0030).</summary>
public sealed record CreateRole(string ActorId, string Name, IReadOnlyCollection<string> Permissions);

public static class CreateRoleHandler
{
    public static async Task<Result<RoleDetails>> HandleAsync(
        CreateRole command,
        ITenantCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var actor = await Actor.FindAsync(catalog, command.ActorId, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        var role = Role.CreateCustom(Guid.CreateVersion7(time.GetUtcNow()), catalog.TenantId, command.Name, command.Permissions);
        if (!role.IsSuccess)
        {
            return role.Error;
        }

        if (RoleGrant.Allows(actor.Value.Permissions, role.Value.Permissions) is { IsSuccess: false } refused)
        {
            return refused.Error;
        }

        if (await catalog.IsRoleNameTakenAsync(role.Value.Name, exceptRoleId: null, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        catalog.Add(role.Value);
        await catalog.SaveChangesAsync(cancellationToken);
        return role.Value.ToDetails();
    }
}
