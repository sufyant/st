using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantDatabaseNameTests
{
    [Fact]
    public void ForTenant_BuildsNameFromTheTenantIdentifier()
    {
        // Arrange
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");

        // Act
        var databaseName = TenantDatabaseName.ForTenant(id);

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", databaseName.Value);
    }

    [Fact]
    public void ForTenant_ForAnEmptyIdentifier_Throws()
    {
        // Arrange & Act
        var act = () => TenantDatabaseName.ForTenant(Guid.Empty);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("control_plane")]
    public void Create_ForAReservedDatabaseName_Throws(string value)
    {
        // Arrange & Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tenant_Upper")]
    [InlineData("tenant-with-dash")]
    [InlineData("tenant name")]
    [InlineData("1tenant")]
    public void Create_ForAnInvalidIdentifier_Throws(string value)
    {
        // Arrange & Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_ForANameLongerThanSixtyThreeBytes_Throws()
    {
        // Arrange
        var value = new string('a', 64);

        // Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_ForAValidName_KeepsTheValue()
    {
        // Arrange
        const string value = "tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9";

        // Act
        var databaseName = TenantDatabaseName.Create(value);

        // Assert
        Assert.Equal(value, databaseName.Value);
    }
}
