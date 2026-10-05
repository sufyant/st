using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        var members = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            scope.GetRequiredService<TenantCatalog>().Memberships.Select(membership => membership.UserId).ToListAsync(Cancellation));

        members.ShouldBe([member.Id]);
    }

    [Fact]
    public async Task A_membership_added_in_a_tenant_belongs_to_that_tenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);

        await InTenant.RunAsync(database.Services, tenant.Id, async scope =>
        {
            var catalog = scope.GetRequiredService<TenantCatalog>();
            catalog.AddMember(user.Id, BuiltInRoles.Member.Id);
            await catalog.SaveChangesAsync(Cancellation);
            return SharedKernel.Result.Success();
        });

        var memberships = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            scope.GetRequiredService<TenantCatalog>().Memberships.Select(membership => membership.TenantId).ToListAsync(Cancellation));
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

    [Fact]
    public async Task A_tenant_finds_the_built_in_roles_and_its_own_custom_roles()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var own = await Catalog.AddCustomRoleAsync(database.Services, tenant);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, async scope =>
        {
            ITenantCatalog catalog = scope.GetRequiredService<TenantCatalog>();
            return (await catalog.FindRoleAsync(own.Id, Cancellation), await catalog.FindRoleAsync(BuiltInRoles.Owner.Id, Cancellation));
        });

        found.Item1.ShouldNotBeNull().Name.ShouldBe(own.Name);
        found.Item2.ShouldNotBeNull().Permissions.ShouldBe(BuiltInRoles.Owner.Permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task A_tenant_does_not_find_the_custom_role_of_another_tenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var theirs = await Catalog.AddCustomRoleAsync(database.Services, other);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            ((ITenantCatalog)scope.GetRequiredService<TenantCatalog>()).FindRoleAsync(theirs.Id, Cancellation));

        found.ShouldBeNull();
    }

    [Fact]
    public async Task A_role_of_another_tenant_cannot_be_added()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var theirs = Role.CreateCustom(Guid.CreateVersion7(), other.Id, "Theirs", []).Value;

        var add = () => InTenant.ReadAsync(database.Services, tenant.Id, scope =>
        {
            ((ITenantCatalog)scope.GetRequiredService<TenantCatalog>()).Add(theirs);
            return Task.FromResult(0);
        });

        await add.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_tenant_does_not_find_the_invitation_of_another_tenant()
    {
        var other = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, other, BuiltInRoles.Owner);
        var email = Unique.Email();
        await Handlers.InviteAndDeliverAsync(database.Services, other.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);
        var tokenHash = InvitationToken.Hash(Handlers.TokenOf(database.Identity.Invitations.Single(invited => invited.Email == email).AcceptLink));
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            ((ITenantCatalog)scope.GetRequiredService<TenantCatalog>()).FindInvitationForUpdateAsync(tokenHash, Cancellation));

        found.ShouldBeNull();
    }

    [Fact]
    public async Task A_tenant_does_not_find_the_invitation_of_another_tenant_by_its_id()
    {
        var other = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, other, BuiltInRoles.Owner);
        var (invitation, _) = await Handlers.InviteAsync(database.Services, other.Id, owner.ExternalId, Unique.Email(), BuiltInRoles.Member.Id);
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            ((ITenantCatalog)scope.GetRequiredService<TenantCatalog>()).FindInvitationForUpdateAsync(invitation.Value.Id, Cancellation));

        found.ShouldBeNull();
    }

    // Onboarding adds the tenant it runs in, and only that one (0026).
    [Fact]
    public async Task A_tenant_cannot_add_another_tenant()
    {
        var other = Domain.Tenants.Tenant.Create(Guid.CreateVersion7(), Unique.Slug()).Value;

        var add = () => InTenant.ReadAsync(database.Services, Guid.CreateVersion7(), scope =>
            ((ITenantCatalog)scope.GetRequiredService<TenantCatalog>()).TryAddAsync(other, Cancellation));

        await add.ShouldThrowAsync<InvalidOperationException>();
    }
}
