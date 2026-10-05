using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
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

        var membership = await FindMembershipAsync(tenant.Slug, user.ExternalId);

        membership.ShouldNotBeNull().TenantId.ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task A_member_resolves_with_the_permissions_of_a_built_in_role()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var admin = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Admin);

        var membership = await FindMembershipAsync(tenant.Slug, admin.ExternalId);

        membership.ShouldNotBeNull().Permissions.ShouldBe(
            [Permissions.MembersInvite, Permissions.MembersManage, Permissions.RolesManage, Permissions.NotificationsSchedule], ignoreOrder: true);
    }

    // Custom roles work without code changes (0030).
    [Fact]
    public async Task A_member_resolves_with_the_permissions_of_a_custom_role()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var recruiter = await Catalog.AddCustomRoleAsync(database.Services, tenant, Permissions.MembersInvite);
        var user = await Catalog.AddMemberAsync(database.Services, tenant, recruiter);

        var membership = await FindMembershipAsync(tenant.Slug, user.ExternalId);

        membership.ShouldNotBeNull().Permissions.ShouldBe([Permissions.MembersInvite]);
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, user);

        var membership = await FindMembershipAsync(tenant.Slug, user.ExternalId);

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_slug_does_not_resolve()
    {
        var user = await Catalog.AddUserAsync(database.Services);

        var membership = await FindMembershipAsync("no-such-tenant", user.ExternalId);

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_user_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var membership = await FindMembershipAsync(tenant.Slug, Unique.ExternalId());

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task A_member_of_a_provisioning_tenant_does_not_resolve()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services, TenantStatus.Provisioning);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, user);

        var membership = await FindMembershipAsync(tenant.Slug, user.ExternalId);

        membership.ShouldBeNull();
    }

    // A system admin enters a tenant without being its member, also while it is provisioning or has failed (0031).
    [Fact]
    public async Task A_tenant_is_found_by_its_slug_whatever_its_status()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services, TenantStatus.Provisioning);

        var found = await FindTenantAsync(tenant.Slug);

        found.ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task An_unknown_slug_finds_no_tenant()
    {
        var found = await FindTenantAsync("no-such-tenant");

        found.ShouldBeNull();
    }

    private async Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMembershipAsync(slug, externalUserId, TestContext.Current.CancellationToken);
    }

    private async Task<Guid?> FindTenantAsync(string slug)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindTenantAsync(slug, TestContext.Current.CancellationToken);
    }
}
