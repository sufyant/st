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
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var admin = PlatformAdmin.Create(externalUserId, createdAt);

        // Assert
        Assert.NotEqual(Guid.Empty, admin.Id.Value);
        Assert.Equal(externalUserId, admin.ExternalUserId);
        Assert.Equal(createdAt, admin.CreatedAt);
    }
}
