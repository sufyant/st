using Domain.ControlPlane.Administration;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class PlatformAdminIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => PlatformAdminId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
