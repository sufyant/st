using System.Text.Json;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Deprovisioning;

public sealed class TenantHardDeleteHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner) : IOutboxMessageHandler
{
    public string MessageType => TenantHardDeleteRequested.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        HandleAsync(
            JsonSerializer.Deserialize<TenantHardDeleteRequested>(payload)
            ?? throw new InvalidOperationException("The outbox payload is empty."),
            cancellationToken);

    public async Task HandleAsync(TenantHardDeleteRequested message, CancellationToken cancellationToken)
    {
        var tenantId = TenantId.From(message.TenantId);
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == tenantId,
                         cancellationToken)
                     ?? throw new InvalidOperationException($"Tenant '{message.TenantId}' does not exist.");

        if (tenant.Status is TenantStatus.Deleted)
        {
            return;
        }

        var credential = await controlPlaneDbContext.TenantCredentials.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenant.Id, cancellationToken);
        // The stored name is authoritative; deriving it would strand roles created under an
        // earlier naming format. A tenant that failed before its credential was written has none.
        var roleName = credential?.RoleName ?? TenantRoleName.ForTenant(tenant.Id);
        await provisioner.DropTenantAsync(tenant.DatabaseName, roleName, cancellationToken);

        if (credential is not null)
        {
            controlPlaneDbContext.TenantCredentials.Remove(credential);
        }

        tenant.MarkDeleted();
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }
}
