using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.IntegrationTests;

// Tenant-owned catalog rows are reached through one access point, and row level security keeps each tenant to its own.
public sealed class TenantCatalogTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadMemberships_InATenant_ReturnsOnlyItsOwn()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var other = await Catalog.AddTenantAsync(database.Services);
        var member = await Catalog.AddUserAsync(database.Services);
        var outsider = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, tenant, member);
        await Catalog.AddMemberAsync(database.Services, other, outsider);

        var members = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            ((TenantCatalog)scope.GetRequiredService<ITenantCatalog>()).Memberships.Select(membership => membership.UserId).ToListAsync(Cancellation));

        members.ShouldBe([member.Id]);
    }

    [Fact]
    public async Task AddMember_InATenant_BelongsToThatTenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var user = await Catalog.AddUserAsync(database.Services);

        await InTenant.RunAsync(database.Services, tenant.Id, async scope =>
        {
            var catalog = ((TenantCatalog)scope.GetRequiredService<ITenantCatalog>());
            catalog.AddMember(user.Id, BuiltInRoles.Member.Id);
            await catalog.SaveChangesAsync(Cancellation);
            return SharedKernel.Result.Success();
        });

        var memberships = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            ((TenantCatalog)scope.GetRequiredService<ITenantCatalog>()).Memberships.Select(membership => membership.TenantId).ToListAsync(Cancellation));
        memberships.ShouldBe([tenant.Id]);
    }

    [Fact]
    public async Task ReadMemberships_OutsideATenant_IsRefused()
    {
        await using var scope = database.Services.CreateAsyncScope();
        var catalog = (TenantCatalog)scope.ServiceProvider.GetRequiredService<ITenantCatalog>();

        var read = () => catalog.Memberships.ToListAsync(Cancellation);

        await read.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FindInvitationByHash_OfAnotherTenant_FindsNothing()
    {
        var email = Unique.Email();
        var (_, _, ready) = await Handlers.OnboardAsync(database.Services, email);
        var tokenHash = InvitationToken.Hash(Handlers.SecretOf(Handlers.CodeOf(ready.Link)));
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            scope.GetRequiredService<ITenantCatalog>().FindInvitationForUpdateAsync(tokenHash, Cancellation));

        found.ShouldBeNull();
    }

    [Fact]
    public async Task FindInvitationById_OfAnotherTenant_FindsNothing()
    {
        var (_, _, ready) = await Handlers.OnboardAsync(database.Services, Unique.Email());
        var invitationId = ready.InvitationId;
        var tenant = await Catalog.AddTenantAsync(database.Services);

        var found = await InTenant.ReadAsync(database.Services, tenant.Id, scope =>
            scope.GetRequiredService<ITenantCatalog>().FindInvitationForUpdateAsync(invitationId, Cancellation));

        found.ShouldBeNull();
    }

    // Onboarding adds the tenant it runs in, and only that one.
    [Fact]
    public async Task AddTenant_OtherThanTheDeclaredTenant_IsRefused()
    {
        var other = Domain.Tenants.Tenant.Create(Guid.CreateVersion7(), "Other Ltd", Unique.Slug()).Value;

        var add = () => InTenant.ReadAsync(database.Services, Guid.CreateVersion7(), scope =>
            scope.GetRequiredService<ITenantCatalog>().TryAddAsync(other, Cancellation));

        await add.ShouldThrowAsync<InvalidOperationException>();
    }
}
