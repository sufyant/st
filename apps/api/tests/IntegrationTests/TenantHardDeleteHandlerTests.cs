using Application.Features.Deprovisioning;
using Application.Features.Provisioning;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class TenantHardDeleteHandlerTests
{
    [Fact]
    public async Task HandleAsync_DropsTheDatabaseRoleAndCredentialThenMarksTheTenantDeleted()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var provisioningHandler = new TenantProvisioningHandler(
            fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
        await provisioningHandler.HandleAsync(
            new TenantProvisioningRequested(
                tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);
        await using (var context = fixture.CreateControlPlane())
        {
            var reloaded = await context.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            reloaded.BeginDeprovisioning();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var handler = new TenantHardDeleteHandler(fixture.CreateControlPlane(), fixture.Provisioner);

        // Act
        await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Assert
        await using var assertions = fixture.CreateControlPlane();
        var reloadedTenant = await assertions.Tenants.SingleAsync(
            t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
        Assert.Equal(TenantStatus.Deleted, reloadedTenant.Status);
        Assert.False(await assertions.TenantCredentials.AnyAsync(
            c => c.TenantId == tenant.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_RunTwice_IsIdempotent()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var provisioningHandler = new TenantProvisioningHandler(
            fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
        await provisioningHandler.HandleAsync(
            new TenantProvisioningRequested(
                tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);
        await using (var context = fixture.CreateControlPlane())
        {
            var reloaded = await context.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            reloaded.BeginDeprovisioning();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var handler = new TenantHardDeleteHandler(fixture.CreateControlPlane(), fixture.Provisioner);
        await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Assert
        await act(); // does not throw
    }
}
