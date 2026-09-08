using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class MembershipTests
{
    [Fact]
    public void Create_ValidInput_SetsProperties()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Act
        var membership = Membership.Create(userId, tenantId, "admin");

        // Assert
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal("admin", membership.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyRole_ThrowsArgumentException(string role)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role));
    }

    [Fact]
    public void ChangeRole_ValidInput_UpdatesRole()
    {
        // Arrange
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "member");

        // Act
        membership.ChangeRole("admin");

        // Assert
        Assert.Equal("admin", membership.Role);
    }
}
