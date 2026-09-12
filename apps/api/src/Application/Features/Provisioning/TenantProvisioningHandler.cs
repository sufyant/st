using System.Text.Json;
using Domain.Shared;
using Domain.Authorization;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Provisioning;

public sealed class TenantProvisioningHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner) : IOutboxMessageHandler
{
    public string MessageType => TenantProvisioningRequested.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        HandleAsync(
            JsonSerializer.Deserialize<TenantProvisioningRequested>(payload)
            ?? throw new InvalidOperationException("The outbox payload is empty."),
            cancellationToken);

    public async Task HandleAsync(TenantProvisioningRequested message, CancellationToken cancellationToken)
    {
        var tenantId = TenantId.From(message.TenantId);
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == tenantId,
                         cancellationToken)
                     ?? throw new InvalidOperationException($"Tenant '{message.TenantId}' does not exist.");

        if (tenant.Status is TenantStatus.Active)
        {
            return;
        }

        var step = TenantProvisioningStep.CreatingDatabase;

        try
        {
            await RecordAsync(tenant, step, cancellationToken);
            await provisioner.CreateDatabaseAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.MigratingSchema;
            await RecordAsync(tenant, step, cancellationToken);
            await provisioner.MigrateSchemaAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.GrantingAccess;
            await RecordAsync(tenant, step, cancellationToken);
            await provisioner.GrantTenantAccessAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.SeedingOwner;
            await RecordAsync(tenant, step, cancellationToken);
            await SeedOwnerAsync(tenant, message.OwnerExternalUserId, message.OwnerEmail, cancellationToken);

            tenant.CompleteProvisioning();
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            tenant.RecordProvisioningFailure(step, exception.Message);
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private async Task RecordAsync(Tenant tenant, TenantProvisioningStep step, CancellationToken cancellationToken)
    {
        tenant.RecordProvisioningProgress(step);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedOwnerAsync(
        Tenant tenant,
        string ownerExternalUserId,
        string ownerEmail,
        CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(ownerExternalUserId);

        await using var tenantDbContext = provisioner.CreateTenantDbContext(tenant.DatabaseName);
        var user = await tenantDbContext.Users
            .Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.ExternalUserId == externalUserId, cancellationToken);

        if (user is null)
        {
            var ownerRole = await tenantDbContext.Roles.SingleAsync(
                candidate => candidate.Code == AccessCatalog.OwnerRole.Code,
                cancellationToken);
            user = User.Create(externalUserId, EmailAddress.Create(ownerEmail), UserStatus.Active, ownerRole);
            tenantDbContext.Users.Add(user);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }
        else if (user.Role.Code != AccessCatalog.OwnerRole.Code)
        {
            var ownerRole = await tenantDbContext.Roles.SingleAsync(
                candidate => candidate.Code == AccessCatalog.OwnerRole.Code,
                cancellationToken);
            user.AssignRole(ownerRole);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasMembership = await controlPlaneDbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            controlPlaneDbContext.Memberships.Add(
                Membership.Create(tenant.Id, externalUserId));
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
