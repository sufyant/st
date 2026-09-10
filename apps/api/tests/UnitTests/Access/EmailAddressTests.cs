using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("two@at@example.com")]
    public void Create_ForAnInvalidAddress_Throws(string value)
    {
        // Arrange & Act
        var act = () => EmailAddress.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_NormalisesCaseAndWhitespace()
    {
        // Arrange & Act
        var email = EmailAddress.Create("  Owner@Example.COM ");

        // Assert
        Assert.Equal("owner@example.com", email.Value);
    }

    [Fact]
    public void Equality_IgnoresTheOriginalCase()
    {
        // Arrange & Act
        var first = EmailAddress.Create("owner@example.com");
        var second = EmailAddress.Create("OWNER@EXAMPLE.COM");

        // Assert
        Assert.Equal(first, second);
    }
}
