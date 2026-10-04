using ControlPlane.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace ControlPlane.IntegrationTests;

public sealed class TenantDirectoryTests(Database database)
{
    [Fact]
    public async Task A_member_of_an_active_tenant_resolves_to_the_tenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, user);

        var resolved = await FindMemberTenantAsync(tenant.Slug, user.ExternalId);

        resolved.ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, user);

        var resolved = await FindMemberTenantAsync(tenant.Slug, user.ExternalId);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_slug_does_not_resolve()
    {
        var user = await Catalog.AddUserAsync(database.Services);

        var resolved = await FindMemberTenantAsync("no-such-tenant", user.ExternalId);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_user_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var resolved = await FindMemberTenantAsync(tenant.Slug, Unique.ExternalId());

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task A_member_of_a_provisioning_tenant_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services, TenantStatus.Provisioning);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, user);

        var resolved = await FindMemberTenantAsync(tenant.Slug, user.ExternalId);

        resolved.ShouldBeNull();
    }

    private async Task<Guid?> FindMemberTenantAsync(string slug, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMemberTenantAsync(slug, externalUserId, TestContext.Current.CancellationToken);
    }
}
