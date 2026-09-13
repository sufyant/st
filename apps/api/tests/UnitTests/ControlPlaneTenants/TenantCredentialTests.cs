using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantCredentialTests
{
    private static readonly TenantId TenantId = TenantId.New();

    private static readonly TenantRoleName RoleName = TenantRoleName.ForTenant(TenantId);

    [Fact]
    public void Create_SetsTheGivenFields()
    {
        // Arrange & Act
        var credential = TenantCredential.Create(TenantId, RoleName, "cipher-text");

        // Assert
        Assert.Equal(TenantId, credential.TenantId);
        Assert.Equal(RoleName, credential.RoleName);
        Assert.Equal("cipher-text", credential.EncryptedPassword);
    }

    [Fact]
    public void Create_RejectsAnEmptyEncryptedPassword()
    {
        // Arrange & Act
        var act = () => TenantCredential.Create(TenantId, RoleName, "");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Rotate_ReplacesTheEncryptedPassword()
    {
        // Arrange
        var credential = TenantCredential.Create(TenantId, RoleName, "old-cipher-text");

        // Act
        credential.Rotate("new-cipher-text");

        // Assert
        Assert.Equal("new-cipher-text", credential.EncryptedPassword);
    }
}
