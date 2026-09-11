using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantTests
{
    [Fact]
    public void Create_DerivesTheDatabaseNameFromTheGeneratedId()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Assert
        Assert.Equal($"tenant_{tenant.Id.Value:N}", tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsInTheProvisioningState()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
    }

    [Fact]
    public void RenameAlias_ReplacesTheAlias()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"));

        // Assert
        Assert.Equal("globex", tenant.Alias.Value);
    }

    [Fact]
    public void RenameAlias_DoesNotChangeTheDatabaseName()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        var databaseName = tenant.DatabaseName.Value;

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"));

        // Assert
        Assert.Equal(databaseName, tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsAtTheFirstProvisioningStep()
    {
        // Arrange & Act
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Assert
        Assert.Equal(TenantProvisioningStep.CreatingDatabase, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
    }

    [Fact]
    public void RecordProvisioningProgress_MovesTheStepAndClearsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.RecordProvisioningFailure(TenantProvisioningStep.CreatingDatabase, "boom");

        // Act
        tenant.RecordProvisioningProgress(TenantProvisioningStep.MigratingSchema);

        // Assert
        Assert.Equal(TenantProvisioningStep.MigratingSchema, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
    }

    [Fact]
    public void RecordProvisioningFailure_KeepsTheTenantProvisioningAndRecordsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Act
        tenant.RecordProvisioningFailure(TenantProvisioningStep.GrantingAccess, "denied");

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(TenantProvisioningStep.GrantingAccess, tenant.ProvisioningStep);
        Assert.Equal("denied", tenant.ProvisioningError);
    }

    [Fact]
    public void CompleteProvisioning_ActivatesTheTenantAndClearsProgress()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.RecordProvisioningFailure(TenantProvisioningStep.SeedingOwner, "boom");

        // Act
        tenant.CompleteProvisioning();

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
    }

    [Fact]
    public void Suspend_FromProvisioning_Throws()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Act
        var act = () => tenant.Suspend();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Resume_FromActive_Throws()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.CompleteProvisioning();

        // Act
        var act = () => tenant.Resume();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void CompleteProvisioning_Twice_Throws()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.CompleteProvisioning();

        // Act
        var act = () => tenant.CompleteProvisioning();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void MarkDeleted_WithoutDeprovisioning_Throws()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.CompleteProvisioning();

        // Act
        var act = () => tenant.MarkDeleted();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void TheLifecycleRunsEndToEnd()
    {
        // Arrange
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Act
        tenant.CompleteProvisioning();
        tenant.Suspend();
        tenant.Resume();
        tenant.BeginDeprovisioning();
        tenant.MarkDeleted();

        // Assert
        Assert.Equal(TenantStatus.Deleted, tenant.Status);
    }
}
