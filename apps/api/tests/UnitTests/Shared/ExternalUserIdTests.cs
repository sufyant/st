using Domain.Shared;
using Xunit;

namespace UnitTests.Shared;

public sealed class ExternalUserIdTests
{
    [Fact]
    public void TheEqualityOperatorComparesTheValue()
    {
        // Arrange
        var left = ExternalUserId.Create("user_2abc123");

        // Act
        var right = ExternalUserId.Create("user_2abc123");

        // Assert
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void DifferentValuesAreNotEqual()
    {
        // Arrange
        var left = ExternalUserId.Create("user_2abc123");

        // Act
        var right = ExternalUserId.Create("user_2def456");

        // Assert
        Assert.True(left != right);
    }

    [Fact]
    public void Create_ForABlankValue_Throws()
    {
        // Arrange
        var value = "   ";

        // Act
        var act = () => ExternalUserId.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
