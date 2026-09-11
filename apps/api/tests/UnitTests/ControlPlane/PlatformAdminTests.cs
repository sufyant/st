using Domain.ControlPlane.Administration;
using Domain.Shared;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class PlatformAdminTests
{
    [Fact]
    public void Create_KeepsTheExternalUserIdentity()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var admin = PlatformAdmin.Create(externalUserId);

        // Assert
        Assert.NotEqual(Guid.Empty, admin.Id.Value);
        Assert.Equal(externalUserId, admin.ExternalUserId);
    }
}
