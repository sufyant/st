using System.Reflection;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.UnitTests;

public class PermissionPoolTests
{
    // A tenant's custom roles choose from the tenant pool, so a system permission must never be part of it.
    [Fact]
    public void The_tenant_and_system_pools_share_no_permission()
    {
        var shared = PermissionPools.TenantPool.Intersect(PermissionPools.SystemPool);

        shared.ShouldBeEmpty();
    }

    [Fact]
    public void Every_system_permission_is_named_under_system()
    {
        var misnamed = PermissionPools.SystemPool.Where(permission => !permission.StartsWith("system.", StringComparison.Ordinal));

        misnamed.ShouldBeEmpty();
    }

    // Endpoints name permissions from the catalogue in Contracts; roles and system admins hold them from the pools in Domain.
    [Fact]
    public void PublishCatalogue_EveryPermission_IsInExactlyTheDomainPools()
    {
        var catalogue = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

        catalogue.ShouldBe(PermissionPools.TenantPool.Concat(PermissionPools.SystemPool), ignoreOrder: true);
    }
}
