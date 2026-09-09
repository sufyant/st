using Domain.Access;
using Domain.Access.Users;
using Xunit;

namespace UnitTests.Access;

public sealed class TenantUserTests
{
    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var act = () => TenantUser.Create(Guid.Empty, externalUserId, TenantUserStatus.Active);

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
        var user = TenantUser.Create(id, externalUserId, TenantUserStatus.Active);

        // Assert
        Assert.Equal(id, user.Id);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(TenantUserStatus.Active, user.Status);
    }

    [Fact]
    public void CreateRole_ForAnEmptyRole_Throws()
    {
        // Arrange & Act
        var act = () => TenantUserRole.Create(Guid.NewGuid(), Guid.Empty);

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
        var assignment = TenantUserRole.Create(userId, roleId);

        // Assert
        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(roleId, assignment.RoleId);
    }
}
