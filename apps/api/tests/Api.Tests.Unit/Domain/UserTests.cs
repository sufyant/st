using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class UserTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesWithUtcDefaultTimeZone()
    {
        // Arrange
        var email = Email.Create("user@example.com");

        // Act
        var user = User.Create("clerk_abc123", email);

        // Assert
        Assert.Equal("clerk_abc123", user.ClerkUserId);
        Assert.Equal(email, user.Email);
        Assert.Equal("UTC", user.TimeZoneId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyClerkUserId_ThrowsArgumentException(string clerkUserId)
    {
        // Arrange
        var email = Email.Create("user@example.com");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => User.Create(clerkUserId, email));
    }

    [Fact]
    public void SetTimeZone_ValidInput_UpdatesTimeZoneId()
    {
        // Arrange
        var user = User.Create("clerk_abc123", Email.Create("user@example.com"));

        // Act
        user.SetTimeZone("Europe/Istanbul");

        // Assert
        Assert.Equal("Europe/Istanbul", user.TimeZoneId);
    }

    [Fact]
    public void SetTimeZone_EmptyInput_ThrowsArgumentException()
    {
        // Arrange
        var user = User.Create("clerk_abc123", Email.Create("user@example.com"));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => user.SetTimeZone(""));
    }
}
