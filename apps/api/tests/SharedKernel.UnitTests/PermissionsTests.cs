namespace SharedKernel.UnitTests;

public class PermissionsTests
{
    // A tenant's custom roles choose from the tenant pool, so a system permission must never be part of it (0030, 0031).
    [Fact]
    public void The_tenant_and_system_pools_share_no_permission()
    {
        var shared = Permissions.TenantPool.Intersect(Permissions.SystemPool);

        shared.ShouldBeEmpty();
    }

    [Fact]
    public void Every_system_permission_is_named_under_system()
    {
        var misnamed = Permissions.SystemPool.Where(permission => !permission.StartsWith("system.", StringComparison.Ordinal));

        misnamed.ShouldBeEmpty();
    }
}
