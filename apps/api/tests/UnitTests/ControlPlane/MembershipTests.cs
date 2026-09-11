using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Domain.Shared;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class MembershipTests
{
    [Fact]
    public void Create_SetsAnActiveMembership()
    {
        // Arrange
        var tenantId = TenantId.New();
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var membership = Membership.Create(tenantId, externalUserId);

        // Assert
        Assert.NotEqual(Guid.Empty, membership.Id.Value);
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
}
