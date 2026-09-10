using System.Reflection;
using Application.Abstractions;
using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void EveryGuardedRequest_NamesAPermissionTheCatalogDefines()
    {
        // Arrange
        var known = SystemAccessCatalog.Permissions.Select(permission => permission.Code).ToHashSet();
        var guarded = typeof(TenantPermissions).Assembly
            .GetTypes()
            .Select(type => (type, attribute: type.GetCustomAttribute<RequiresPermissionAttribute>()))
            .Where(pair => pair.attribute is not null)
            .ToList();

        // Act
        var unknown = guarded
            .Where(pair => !known.Contains(pair.attribute!.Permission))
            .Select(pair => $"{pair.type.Name} -> {pair.attribute!.Permission}")
            .ToList();

        // Assert
        Assert.NotEmpty(guarded);
        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryPermissionConstant_MatchesTheCatalog()
    {
        // Arrange
        var known = SystemAccessCatalog.Permissions.Select(permission => permission.Code).ToHashSet();

        // Act
        var declared = typeof(TenantPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        // Assert
        Assert.Equal(known.OrderBy(code => code), declared.OrderBy(code => code));
    }
}
