using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@Example.COM")]
    [InlineData("  user@example.com  ")]
    public void Create_ValidInput_NormalizesToLowercaseTrimmed(string input)
    {
        // Act
        var email = Email.Create(input);

        // Assert
        Assert.Equal("user@example.com", email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    public void Create_InvalidInput_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Email.Create(input));
    }

    [Fact]
    public void Equals_SameValueDifferentCase_ReturnsTrue()
    {
        // Arrange
        var first = Email.Create("user@example.com");
        var second = Email.Create("USER@EXAMPLE.COM");

        // Act & Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_NullInput_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Email.Create(null!));
    }

    [Fact]
    public void EqualityOperator_StructurallyEqualInstances_ReturnsTrue()
    {
        // Arrange
        var first = Email.Create("user@example.com");
        var second = Email.Create("user@example.com");

        // Act & Assert
        Assert.True(first == second);
    }
}
