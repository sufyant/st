using Domain.Shared;
using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class UserTests
{
    [Fact]
    public void Create_KeepsTheIdentityStatusAndRole()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var email = EmailAddress.Create("user@example.com");
        var role = new Role();

        // Act
        var user = User.Create(externalUserId, email, UserStatus.Active, role);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id.Value);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(email, user.Email);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Same(role, user.Role);
    }

    [Fact]
    public void AssignRole_ReplacesThePreviousOne()
    {
        // Arrange
        var owner = new Role();
        var member = new Role();
        var user = User.Create(ExternalUserId.Create("user_2abc123"), EmailAddress.Create("user@example.com"), UserStatus.Active, owner);

        // Act
        user.AssignRole(member);

        // Assert
        Assert.Same(member, user.Role);
    }

    [Fact]
    public void AssignRole_WithNull_Throws()
    {
        // Arrange
        var user = User.Create(ExternalUserId.Create("user_2abc123"), EmailAddress.Create("user@example.com"), UserStatus.Active, new Role());

        // Act
        var act = () => user.AssignRole(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }
}
