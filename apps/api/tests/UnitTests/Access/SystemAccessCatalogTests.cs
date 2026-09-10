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

    [Fact]
    public void Roles_ContainAMemberRoleWithReadOnlyPermissions()
    {
        // Arrange
        var readOnlyCodes = new[] { "members.read", "roles.read" };

        // Act
        var member = SystemAccessCatalog.Roles.Single(role => role.Code == "member");

        // Assert
        var granted = SystemAccessCatalog.Permissions
            .Where(permission => member.PermissionIds.Contains(permission.Id))
            .Select(permission => permission.Code)
            .OrderBy(code => code);
        Assert.Equal(readOnlyCodes.OrderBy(code => code), granted);
    }

    [Fact]
    public void Roles_GiveTheOwnerEveryPermission()
    {
        // Arrange & Act
        var owner = SystemAccessCatalog.Roles.Single(role => role.Code == "owner");

        // Assert
        Assert.Equal(SystemAccessCatalog.Permissions.Count, owner.PermissionIds.Count);
    }
}
