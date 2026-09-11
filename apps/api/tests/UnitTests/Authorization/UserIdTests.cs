using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class UserIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => UserId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void New_ProducesDistinctIdentifiers()
    {
        // Arrange & Act
        var first = UserId.New();
        var second = UserId.New();

        // Assert
        Assert.NotEqual(first, second);
    }
}
