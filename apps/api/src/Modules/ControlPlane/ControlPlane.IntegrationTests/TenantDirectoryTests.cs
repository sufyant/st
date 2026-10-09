using ControlPlane.Contracts;
using ControlPlane.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.IntegrationTests;

public sealed class TenantDirectoryTests(Database database)
{
    [Fact]
    public async Task ResolveTenant_MemberOfAnActiveTenant_FindsTheMembership()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, user);

        var membership = await FindMembershipAsync(tenant.Id, user.ExternalId);

        membership.ShouldNotBeNull().TenantId.ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task ResolveTenant_UserNotAMember_FindsNothing()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, user);

        var membership = await FindMembershipAsync(tenant.Id, user.ExternalId);

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveTenant_UnknownTenant_FindsNothing()
    {
        var user = await Catalog.AddUserAsync(database.Services);

        var membership = await FindMembershipAsync(Guid.CreateVersion7(), user.ExternalId);

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveTenant_UnknownUser_FindsNothing()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var membership = await FindMembershipAsync(tenant.Id, Unique.ExternalId());

        membership.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveTenant_MemberOfAProvisioningTenant_FindsNothing()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services, TenantStatus.Provisioning);
        var user = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, user);

        var membership = await FindMembershipAsync(tenant.Id, user.ExternalId);

        membership.ShouldBeNull();
    }

    private async Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMembershipAsync(tenantId, externalUserId, TestContext.Current.CancellationToken);
    }
}
