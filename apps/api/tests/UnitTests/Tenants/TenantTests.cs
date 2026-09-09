using Domain.Tenants;
using Xunit;

namespace UnitTests.Tenants;

public sealed class TenantTests
{
    [Fact]
    public void Create_UsesTenantIdInNFormatAsDatabaseName()
    {
        // Arrange
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
        var alias = TenantAlias.Create("acme");

        // Act
        var tenant = Tenant.Create(id, alias);

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", tenant.DatabaseName);
    }

    [Fact]
    public void RenameAlias_ChangesAliasWithoutChangingDatabaseName()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));

        // Act
        tenant.RenameAlias(TenantAlias.Create("acme-finance"));

        // Assert
        Assert.Equal("acme-finance", tenant.Alias.Value);
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", tenant.DatabaseName);
    }

    [Fact]
    public void Create_WithEmptyId_ThrowsArgumentException()
    {
        // Arrange
        var alias = TenantAlias.Create("acme");

        // Act
        var action = () => Tenant.Create(Guid.Empty, alias);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }
}
