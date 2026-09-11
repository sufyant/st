using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class RoleIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => RoleId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
