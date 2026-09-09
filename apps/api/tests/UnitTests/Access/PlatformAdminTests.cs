using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class PlatformAdminTests
{
    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var act = () => PlatformAdmin.Create(Guid.Empty, externalUserId, DateTimeOffset.UtcNow);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_KeepsTheExternalUserIdentity()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var admin = PlatformAdmin.Create(Guid.NewGuid(), externalUserId, createdAt);

        // Assert
        Assert.Equal(externalUserId, admin.ExternalUserId);
        Assert.Equal(createdAt, admin.CreatedAt);
    }
}
