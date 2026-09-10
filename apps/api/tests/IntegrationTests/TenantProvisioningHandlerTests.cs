using Application.Features.Provisioning;
using Domain.Access;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisioningHandlerTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Username=nobody;Password=nobody";

    [Fact]
    public async Task HandleAsync_ProvisionsTheTenantAndActivatesIt()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningStep);
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users"));
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM user_roles"));
    }

    [Fact]
    public async Task HandleAsync_CreatesTheOwnerMembership()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await using var verification = fixture.CreateControlPlane();
        var userId = ExternalUserId.Create(ProvisioningFixture.OwnerExternalUserId);
        Assert.True(await verification.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == userId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_RunTwice_LeavesTheSameResult()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);
        var message = new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId);
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Act
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users"));
        await using var verification = fixture.CreateControlPlane();
        Assert.Equal(1, await verification.Memberships.CountAsync(
            membership => membership.TenantId == tenant.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownTenant_Throws()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(Guid.CreateVersion7(), ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task HandleAsync_WhenAStepFails_RecordsTheStepAndRethrows()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(
            context,
            new TenantProvisioner(Unreachable),
            TimeProvider.System);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(act);
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Provisioning, reloaded.Status);
        Assert.Equal(TenantProvisioningStep.CreatingDatabase, reloaded.ProvisioningStep);
        Assert.NotNull(reloaded.ProvisioningError);
    }

    [Fact]
    public async Task HandleAsync_AfterAFailure_CompletesOnRetry()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var message = new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId);

        await using (var failingContext = fixture.CreateControlPlane())
        {
            var failingHandler = new TenantProvisioningHandler(
                failingContext,
                new TenantProvisioner(Unreachable),
                TimeProvider.System);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await failingHandler.HandleAsync(message, TestContext.Current.CancellationToken));
        }

        // Act
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningError);
    }
}
