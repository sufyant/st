using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantAliasTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-01")]
    public void Create_WithValidPathAlias_ReturnsAlias(string value)
    {
        // Arrange

        // Act
        var alias = TenantAlias.Create(value);

        // Assert
        Assert.Equal(value, alias.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("Acme")]
    [InlineData("acme_")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("health")]
    [InlineData("hubs")]
    [InlineData("metrics")]
    [InlineData("openapi")]
    [InlineData("swagger")]
    [InlineData("docs")]
    [InlineData("system")]
    [InlineData("acme\n")]
    public void Create_WithInvalidPathAlias_ThrowsArgumentException(string value)
    {
        // Arrange

        // Act
        var action = () => TenantAlias.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }
}
