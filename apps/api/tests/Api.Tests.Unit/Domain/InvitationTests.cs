using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class InvitationTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesAndGeneratesToken()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = Email.Create("invitee@example.com");

        // Act
        var invitation = Invitation.Create(tenantId, email, "member");

        // Assert
        Assert.Equal(tenantId, invitation.TenantId);
        Assert.Equal(email, invitation.Email);
        Assert.Equal("member", invitation.Role);
        Assert.False(string.IsNullOrWhiteSpace(invitation.Token));
    }

    [Fact]
    public void Create_CalledTwice_GeneratesDifferentTokens()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = Email.Create("invitee@example.com");

        // Act
        var first = Invitation.Create(tenantId, email, "member");
        var second = Invitation.Create(tenantId, email, "member");

        // Assert
        Assert.NotEqual(first.Token, second.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyRole_ThrowsArgumentException(string role)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => Invitation.Create(Guid.NewGuid(), Email.Create("invitee@example.com"), role));
    }
}
