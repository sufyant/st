using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class RolePermissionTests
{
    [Fact]
    public void Create_ValidInput_SetsProperties()
    {
        // Act
        var rolePermission = RolePermission.Create("admin", "tenant.invite");

        // Assert
        Assert.Equal("admin", rolePermission.Role);
        Assert.Equal("tenant.invite", rolePermission.Permission);
    }

    [Theory]
    [InlineData("", "tenant.invite")]
    [InlineData("admin", "")]
    public void Create_EmptyRoleOrPermission_ThrowsArgumentException(string role, string permission)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => RolePermission.Create(role, permission));
    }
}
