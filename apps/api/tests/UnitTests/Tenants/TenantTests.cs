using Domain.Tenants;
using Xunit;

namespace UnitTests.Tenants;

public sealed class TenantTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var alias = TenantAlias.Create("acme");

        // Act
        var act = () => Tenant.Create(Guid.Empty, alias, CreatedAt);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_DerivesTheDatabaseNameOnce()
    {
        // Arrange
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");

        // Act
        var tenant = Tenant.Create(id, TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsInTheProvisioningState()
    {
        // Arrange & Act
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(CreatedAt, tenant.CreatedAt);
        Assert.Equal(CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void RenameAlias_ReplacesTheAliasAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
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
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
        var tenant = Tenant.Create(id, TenantAlias.Create("acme"), CreatedAt);

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"), CreatedAt.AddMinutes(5));

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", tenant.DatabaseName.Value);
    }

    [Fact]
    public void ChangeStatus_ReplacesTheStatusAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
        var activatedAt = CreatedAt.AddSeconds(30);

        // Act
        tenant.ChangeStatus(TenantStatus.Active, activatedAt);

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(activatedAt, tenant.UpdatedAt);
    }
}
