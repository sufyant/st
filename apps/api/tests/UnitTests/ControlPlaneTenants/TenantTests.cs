using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_DerivesTheDatabaseNameFromTheGeneratedId()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal($"tenant_{tenant.Id.Value:N}", tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsInTheProvisioningState()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(CreatedAt, tenant.CreatedAt);
        Assert.Equal(CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void RenameAlias_ReplacesTheAliasAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
        var renamedAt = CreatedAt.AddMinutes(5);

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"), renamedAt);

        // Assert
        Assert.Equal("globex", tenant.Alias.Value);
        Assert.Equal(renamedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void RenameAlias_DoesNotChangeTheDatabaseName()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
        var databaseName = tenant.DatabaseName.Value;

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"), CreatedAt.AddMinutes(5));

        // Assert
        Assert.Equal(databaseName, tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsAtTheFirstProvisioningStep()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal(TenantProvisioningStep.CreatingDatabase, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
    }

    [Fact]
    public void RecordProvisioningProgress_MovesTheStepAndClearsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
        tenant.RecordProvisioningFailure(TenantProvisioningStep.CreatingDatabase, "boom", CreatedAt);

        // Act
        tenant.RecordProvisioningProgress(TenantProvisioningStep.MigratingSchema, CreatedAt.AddSeconds(1));

        // Assert
        Assert.Equal(TenantProvisioningStep.MigratingSchema, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
        Assert.Equal(CreatedAt.AddSeconds(1), tenant.UpdatedAt);
    }

    [Fact]
    public void RecordProvisioningFailure_KeepsTheTenantProvisioningAndRecordsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

        // Act
        tenant.RecordProvisioningFailure(TenantProvisioningStep.GrantingAccess, "denied", CreatedAt.AddSeconds(2));

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(TenantProvisioningStep.GrantingAccess, tenant.ProvisioningStep);
        Assert.Equal("denied", tenant.ProvisioningError);
    }

    [Fact]
    public void CompleteProvisioning_ActivatesTheTenantAndClearsProgress()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
        tenant.RecordProvisioningFailure(TenantProvisioningStep.SeedingOwner, "boom", CreatedAt);

        // Act
        tenant.CompleteProvisioning(CreatedAt.AddSeconds(3));

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
        Assert.Equal(CreatedAt.AddSeconds(3), tenant.UpdatedAt);
    }

    [Fact]
    public void ChangeStatus_ReplacesTheStatusAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
        var activatedAt = CreatedAt.AddSeconds(30);

        // Act
        tenant.ChangeStatus(TenantStatus.Active, activatedAt);

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(activatedAt, tenant.UpdatedAt);
    }
}
