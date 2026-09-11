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
    public void CreateRole_KeepsBothIdentifiers()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();

        // Act
        var assignment = UserRole.Create(userId, roleId);

        // Assert
        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(roleId, assignment.RoleId);
    }
}
