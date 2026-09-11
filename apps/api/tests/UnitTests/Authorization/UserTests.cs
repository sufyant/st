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

        // Act
        var user = User.Create(externalUserId, UserStatus.Active);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id.Value);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void AssignRoles_ReplacesTheWholeSet()
    {
        // Arrange
        var user = User.Create(ExternalUserId.Create("user_2abc123"), UserStatus.Active);
        var owner = new Role();
        var member = new Role();
        user.AssignRoles([owner]);

        // Act
        user.AssignRoles([member]);

        // Assert
        Assert.Single(user.Roles);
        Assert.Same(member, user.Roles.Single());
    }

    [Fact]
    public void AssignRoles_WithAnEmptySet_LeavesNoRoles()
    {
        // Arrange
        var user = User.Create(ExternalUserId.Create("user_2abc123"), UserStatus.Active);
        user.AssignRoles([new Role()]);

        // Act
        user.AssignRoles([]);

        // Assert
        Assert.Empty(user.Roles);
    }
}
