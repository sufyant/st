using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Unit.Infrastructure;

public class SchemaAwareModelCacheKeyFactoryTests
{
    private static TenantDbContext CreateTenantContext(string schemaName)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().Options;
        return new TenantDbContext(options, schemaName);
    }

    [Fact]
    public void Create_ReturnsDifferentKeys_ForDifferentSchemaNames()
    {
        var factory = new SchemaAwareModelCacheKeyFactory();
        using var tenantA = CreateTenantContext("tenant_a");
        using var tenantB = CreateTenantContext("tenant_b");

        var keyA = factory.Create(tenantA, designTime: false);
        var keyB = factory.Create(tenantB, designTime: false);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Create_ReturnsEqualKeys_ForSameSchemaNameAndDesignTime()
    {
        var factory = new SchemaAwareModelCacheKeyFactory();
        using var first = CreateTenantContext("tenant_shared");
        using var second = CreateTenantContext("tenant_shared");

        var keyFirst = factory.Create(first, designTime: false);
        var keySecond = factory.Create(second, designTime: false);

        Assert.Equal(keyFirst, keySecond);
    }

    [Fact]
    public void Create_ReturnsDifferentKeys_ForDifferentNonTenantContextTypes()
    {
        var factory = new SchemaAwareModelCacheKeyFactory();
        using var admin = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().Options);
        using var other = new OtherFakeDbContext(new DbContextOptionsBuilder<OtherFakeDbContext>().Options);

        var adminKey = factory.Create(admin, designTime: false);
        var otherKey = factory.Create(other, designTime: false);

        Assert.NotEqual(adminKey, otherKey);
    }

    private sealed class OtherFakeDbContext(DbContextOptions<OtherFakeDbContext> options) : DbContext(options);
}
