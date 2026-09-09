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
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var membership = Membership.Create(membershipId, tenantId, externalUserId);

        // Assert
        Assert.Equal(membershipId, membership.Id);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(ExternalUserId.Create("user_2abc123"), membership.ExternalUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankExternalUserId_ThrowsArgumentException(string value)
    {
        // Arrange
        var action = () => ExternalUserId.Create(value);

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Create_WithEmptyRequiredId_ThrowsArgumentException(bool emptyMembershipId, bool emptyTenantId)
    {
        // Arrange
        var membershipId = emptyMembershipId ? Guid.Empty : Guid.NewGuid();
        var tenantId = emptyTenantId ? Guid.Empty : Guid.NewGuid();
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var action = () => Membership.Create(membershipId, tenantId, externalUserId);

        // Act
        var exception = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }
}
