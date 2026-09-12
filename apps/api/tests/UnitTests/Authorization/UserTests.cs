using Domain.Shared;
using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class UserTests
{
    [Fact]
    public void Create_KeepsTheIdentityAndStatus()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var email = EmailAddress.Create("user@example.com");

        // Act
        var user = User.Create(externalUserId, email, UserStatus.Active);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id.Value);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(email, user.Email);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void AssignRole_ReplacesThePreviousOne()
    {
        // Arrange
        var user = User.Create(ExternalUserId.Create("user_2abc123"), EmailAddress.Create("user@example.com"), UserStatus.Active);
        var owner = new Role();
        var member = new Role();
        user.AssignRole(owner);

        // Act
        user.AssignRole(member);

        // Assert
        Assert.Same(member, user.Role);
    }

    [Fact]
    public void AssignRole_WithNull_LeavesNoRole()
    {
        // Arrange
        var user = User.Create(ExternalUserId.Create("user_2abc123"), EmailAddress.Create("user@example.com"), UserStatus.Active);
        user.AssignRole(new Role());

        // Act
        user.AssignRole(null);

        // Assert
        Assert.Null(user.Role);
    }
}
