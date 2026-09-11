using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => TenantId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void From_KeepsTheValue()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var id = TenantId.From(value);

        // Assert
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void New_ProducesDistinctSortableIdentifiers()
    {
        // Arrange & Act
        var first = TenantId.New();
        var second = TenantId.New();

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(Guid.Empty, first.Value);
    }
}
