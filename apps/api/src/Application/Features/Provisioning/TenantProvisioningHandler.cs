using System.Text.Json;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Provisioning;

public sealed class TenantProvisioningHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner,
    TimeProvider timeProvider) : IOutboxMessageHandler
{
    public string MessageType => TenantProvisioningRequested.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        HandleAsync(
            JsonSerializer.Deserialize<TenantProvisioningRequested>(payload)
            ?? throw new InvalidOperationException("The outbox payload is empty."),
            cancellationToken);

    public async Task HandleAsync(TenantProvisioningRequested message, CancellationToken cancellationToken)
    {
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == message.TenantId,
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
            await SeedOwnerAsync(tenant, message.OwnerExternalUserId, cancellationToken);

            tenant.CompleteProvisioning(timeProvider.GetUtcNow());
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            tenant.RecordProvisioningFailure(step, exception.Message, timeProvider.GetUtcNow());
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private async Task RecordAsync(Tenant tenant, TenantProvisioningStep step, CancellationToken cancellationToken)
    {
        tenant.RecordProvisioningProgress(step, timeProvider.GetUtcNow());
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedOwnerAsync(Tenant tenant, string ownerExternalUserId, CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(ownerExternalUserId);
        var ownerRole = SystemAccessCatalog.Roles.Single(role => role.Code == "owner");

        await using var tenantDbContext = provisioner.CreateTenantDbContext(tenant.DatabaseName);
        var user = await tenantDbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalUserId == externalUserId,
            cancellationToken);

        if (user is null)
        {
            user = TenantUser.Create(Guid.CreateVersion7(), externalUserId, TenantUserStatus.Active);
            tenantDbContext.Users.Add(user);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasRole = await tenantDbContext.UserRoles.AnyAsync(
            assignment => assignment.UserId == user.Id && assignment.RoleId == ownerRole.Id,
            cancellationToken);

        if (!hasRole)
        {
            tenantDbContext.UserRoles.Add(TenantUserRole.Create(user.Id, ownerRole.Id));
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasMembership = await controlPlaneDbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            controlPlaneDbContext.Memberships.Add(
                Membership.Create(Guid.CreateVersion7(), tenant.Id, externalUserId));
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
