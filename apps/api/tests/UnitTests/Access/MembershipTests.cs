using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class MembershipTests
{
    [Fact]
    public void Create_SetsAnActiveMembership()
    {
        // Arrange
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = IdentityUserId.Create("user_2abc123");
        var roleId = Guid.NewGuid();

        // Act
        var membership = Membership.Create(membershipId, tenantId, userId, roleId);

        // Assert
        Assert.Equal(membershipId, membership.Id);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(IdentityUserId.Create("user_2abc123"), membership.UserId);
        Assert.Equal(roleId, membership.RoleId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankIdentityUserId_ThrowsArgumentException(string value)
    {
        // Arrange
        var action = () => IdentityUserId.Create(value);

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Create_WithEmptyRequiredId_ThrowsArgumentException(bool emptyMembershipId, bool emptyTenantId, bool emptyRoleId)
    {
        // Arrange
        var membershipId = emptyMembershipId ? Guid.Empty : Guid.NewGuid();
        var tenantId = emptyTenantId ? Guid.Empty : Guid.NewGuid();
        var roleId = emptyRoleId ? Guid.Empty : Guid.NewGuid();
        var userId = IdentityUserId.Create("user_2abc123");
        var action = () => Membership.Create(membershipId, tenantId, userId, roleId);

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }
}
