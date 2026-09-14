using Application.Features.Provisioning;
using Domain.Shared;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence;
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
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, fixture.DataProtectionProvider);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningStep);
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users"));
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users WHERE role_id IS NOT NULL"));
        var ownerCreatedAt = await fixture.GetUserCreatedAtAsync(
            tenant.DatabaseName.Value,
            ProvisioningFixture.OwnerExternalUserId);
        Assert.NotEqual(default, ownerCreatedAt);
    }

    [Fact]
    public async Task HandleAsync_CreatesTheOwnerMembership()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, fixture.DataProtectionProvider);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);

        // Assert
        await using var verification = fixture.CreateControlPlane();
        var userId = ExternalUserId.Create(ProvisioningFixture.OwnerExternalUserId);
        Assert.True(await verification.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == userId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_StoresAnEncryptedCredentialForTheTenant()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var handler = new TenantProvisioningHandler(
            fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
        var message = new TenantProvisioningRequested(
            tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail);

        // Act
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        await using var context = fixture.CreateControlPlane();
        var credential = await context.TenantCredentials.SingleAsync(
            c => c.TenantId == tenant.Id, TestContext.Current.CancellationToken);
        Assert.Equal(TenantRoleName.ForTenant(tenant.Id).Value, credential.RoleName.Value);
        var protector = fixture.DataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var plainTextPassword = protector.Unprotect(credential.EncryptedPassword);
        Assert.False(string.IsNullOrWhiteSpace(plainTextPassword));
    }

    [Fact]
    public async Task HandleAsync_RunTwice_LeavesTheSameResult()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, fixture.DataProtectionProvider);
        var message = new TenantProvisioningRequested(tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail);
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
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, fixture.DataProtectionProvider);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(Guid.CreateVersion7(), ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
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
            new TenantProvisioner(Unreachable, new AuditInterceptor(TimeProvider.System)),
            fixture.DataProtectionProvider);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
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
        var message = new TenantProvisioningRequested(tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail);

        await using (var failingContext = fixture.CreateControlPlane())
        {
            var failingHandler = new TenantProvisioningHandler(
                failingContext,
                new TenantProvisioner(Unreachable, new AuditInterceptor(TimeProvider.System)),
                fixture.DataProtectionProvider);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await failingHandler.HandleAsync(message, TestContext.Current.CancellationToken));
        }

        // Act
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, fixture.DataProtectionProvider);
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningError);
    }
}
