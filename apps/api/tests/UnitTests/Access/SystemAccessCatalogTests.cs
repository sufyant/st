using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class SystemAccessCatalogTests
{
    [Fact]
    public void Owner_HasEverySystemPermission()
    {
        // Arrange
        var owner = SystemAccessCatalog.Roles.Single(role => role.Code == "owner");

        // Act
        var permissionIds = owner.PermissionIds;

        // Assert
        Assert.Equal(SystemAccessCatalog.Permissions.Select(permission => permission.Id).ToHashSet(), permissionIds);
    }
}
