using Domain.Shared;
using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class UserTests
{
    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var act = () => User.Create(Guid.Empty, externalUserId, UserStatus.Active);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_KeepsTheIdentityAndStatus()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var id = Guid.NewGuid();

        // Act
        var user = User.Create(id, externalUserId, UserStatus.Active);

        // Assert
        Assert.Equal(id, user.Id);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void CreateRole_ForAnEmptyRole_Throws()
    {
        // Arrange & Act
        var act = () => UserRole.Create(Guid.NewGuid(), Guid.Empty);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void CreateRole_KeepsBothIdentifiers()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        // Act
        var assignment = UserRole.Create(userId, roleId);

        // Assert
        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(roleId, assignment.RoleId);
    }
}
