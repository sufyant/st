using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class AccessCatalogTests
{
    [Fact]
    public void Owner_HasEverySystemPermission()
    {
        // Arrange
        var owner = AccessCatalog.Roles.Single(role => role.Code == "owner");

        // Act
        var permissionIds = owner.PermissionIds;

        // Assert
        Assert.Equal(AccessCatalog.Permissions.Select(permission => permission.Id).ToHashSet(), permissionIds);
    }

    [Fact]
    public void Roles_ContainAMemberRoleWithReadOnlyPermissions()
    {
        // Arrange
        var readOnlyCodes = new[] { "members.read", "roles.read" };

        // Act
        var member = AccessCatalog.Roles.Single(role => role.Code == "member");

        // Assert
        var granted = AccessCatalog.Permissions
            .Where(permission => member.PermissionIds.Contains(permission.Id))
            .Select(permission => permission.Code)
            .OrderBy(code => code);
        Assert.Equal(readOnlyCodes.OrderBy(code => code), granted);
    }

    [Fact]
    public void Roles_GiveTheOwnerEveryPermission()
    {
        // Arrange & Act
        var owner = AccessCatalog.Roles.Single(role => role.Code == "owner");

        // Assert
        Assert.Equal(AccessCatalog.Permissions.Count, owner.PermissionIds.Count);
    }

    [Fact]
    public void OwnerRoleIsNamedAndHoldsEveryPermission()
    {
        // Arrange
        var permissionIds = AccessCatalog.Permissions.Select(permission => permission.Id).ToHashSet();

        // Act
        var owner = AccessCatalog.OwnerRole;

        // Assert
        Assert.Equal("owner", owner.Code);
        Assert.Equal(permissionIds, owner.PermissionIds);
    }

    [Fact]
    public void MemberRoleIsNamedAndReadsOnly()
    {
        // Arrange & Act
        var member = AccessCatalog.MemberRole;

        // Assert
        Assert.Equal("member", member.Code);
        Assert.Equal(2, member.PermissionIds.Count);
    }

    [Fact]
    public void EveryRoleCarriesACode()
    {
        // Arrange & Act
        var codes = AccessCatalog.Roles.Select(role => role.Code).ToList();

        // Assert
        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
    }
}
