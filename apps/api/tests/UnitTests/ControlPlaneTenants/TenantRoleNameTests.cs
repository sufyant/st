using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantRoleNameTests
{
    [Fact]
    public void ForTenant_ProducesAnAccessPrefixedLowercaseIdentifier()
    {
        // Arrange
        var tenantId = TenantId.From(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"));

        // Act
        var roleName = TenantRoleName.ForTenant(tenantId);

        // Assert
        Assert.Equal("access_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", roleName.Value);
    }

    [Fact]
    public void Create_RejectsUppercase()
    {
        // Arrange & Act
        var act = () => TenantRoleName.Create("Access_Bad");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_RejectsEmpty()
    {
        // Arrange & Act
        var act = () => TenantRoleName.Create("");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_AcceptsAValidIdentifier()
    {
        // Arrange & Act
        var roleName = TenantRoleName.Create("access_abc123");

        // Assert
        Assert.Equal("access_abc123", roleName.Value);
    }
}
