using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// The catalog has no row level security; tenant-owned catalog rows are isolated by the one access point that reaches them (0021).
public sealed class TenantCatalogTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tenant_sees_only_its_own_memberships()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var member = await Catalog.AddUserAsync(database.Services);
        var outsider = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, member);
        await Catalog.AddMemberAsync(database.Services, other, outsider);

        var members = await InTenantAsync(tenant.Id, catalog => catalog.Memberships.Select(membership => membership.UserId).ToListAsync(Cancellation));

        members.ShouldBe([member.Id]);
    }

    [Fact]
    public async Task A_membership_added_in_a_tenant_belongs_to_that_tenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);

        await InTenantAsync(tenant.Id, async catalog =>
        {
            catalog.AddMember(user.Id);
            return await catalog.SaveChangesAsync(Cancellation);
        });

        var memberships = await InTenantAsync(tenant.Id, catalog => catalog.Memberships.Select(membership => membership.TenantId).ToListAsync(Cancellation));
        memberships.ShouldBe([tenant.Id]);
    }

    [Fact]
    public async Task Outside_a_tenant_the_tenant_catalog_cannot_be_used()
    {
        await using var scope = database.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<TenantCatalog>();

        var read = () => catalog.Memberships.ToListAsync(Cancellation);

        await read.ShouldThrowAsync<InvalidOperationException>();
    }

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<TenantCatalog, Task<T>> work)
    {
        await using var scope = database.Services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, Cancellation);

        var result = await work(scope.ServiceProvider.GetRequiredService<TenantCatalog>());

        await transaction.CommitAsync(Cancellation);
        return result;
    }
}
