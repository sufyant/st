using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ControlPlane.IntegrationTests;

public sealed class InvitationTests(Database database)
{
    [Fact]
    public async Task AcceptInvitation_NewUser_CreatesTheUserAndTheMembershipWithTheInvitedRole()
    {
        var (tenantId, email, code) = await InviteOwnerAsync();
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);

        var accepted = await Handlers.AcceptAsync(database.Services, code, invitee);

        accepted.Value.ShouldBe(new TenantSummary(tenantId, "Acme Ltd", await ScalarAsync<string>($"SELECT slug FROM catalog.tenants WHERE id = '{tenantId}'")));
        (await FindMembershipAsync(tenantId, invitee)).ShouldNotBeNull();
        (await ScalarAsync<string>(
            $"""
            SELECT roles.built_in FROM catalog.memberships
            JOIN catalog.users ON users.id = memberships.user_id JOIN catalog.roles ON roles.id = memberships.role_id
            WHERE users.external_id = '{invitee}'
            """)).ShouldBe("Owner");
    }

    [Fact]
    public async Task AcceptInvitation_ExistingUser_JoinsAnotherTenant()
    {
        var other = await Catalog.AddTenantAsync(database.Services);
        var invitee = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, invitee);
        var (tenantId, email, code) = await InviteOwnerAsync();
        database.Identity.AddAccount(invitee.ExternalId, email);

        await Handlers.AcceptAsync(database.Services, code, invitee.ExternalId);

        (await FindMembershipAsync(tenantId, invitee.ExternalId)).ShouldNotBeNull();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{invitee.ExternalId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task AcceptInvitation_Rejected_LeavesNoUserBehind()
    {
        var (_, _, code) = await InviteOwnerAsync();
        var stranger = Unique.ExternalId();
        database.Identity.AddAccount(stranger, Unique.Email());

        var accepted = await Handlers.AcceptAsync(database.Services, code, stranger);

        accepted.Error.Code.ShouldBe("invitation.email_mismatch");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{stranger}'")).ShouldBe(0);
    }

    [Fact]
    public async Task AcceptInvitation_CodeUsedTwice_IsRejected()
    {
        var (tenantId, email, code) = await InviteOwnerAsync();
        var first = Unique.ExternalId();
        var second = Unique.ExternalId();
        database.Identity.AddAccount(first, email);
        database.Identity.AddAccount(second, email);
        await Handlers.AcceptAsync(database.Services, code, first);

        var reused = await Handlers.AcceptAsync(database.Services, code, second);

        reused.Error.Code.ShouldBe("invitation.not_pending");
        (await FindMembershipAsync(tenantId, second)).ShouldBeNull();
    }

    [Fact]
    public async Task AcceptInvitation_AfterItExpires_IsRejected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time)));
        var (tenantId, email, code) = await InviteOwnerAsync(services);
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);
        time.Advance(TimeSpan.FromDays(7));

        var accepted = await Handlers.AcceptAsync(services, code, invitee);

        accepted.Error.Code.ShouldBe("invitation.expired");
        (await FindMembershipAsync(tenantId, invitee)).ShouldBeNull();
    }

    // A cancelled invitation answers like one that does not exist.
    [Fact]
    public async Task AcceptInvitation_Cancelled_IsNotFound()
    {
        var (tenantId, email, code) = await InviteOwnerAsync();
        var invitationId = await ScalarAsync<Guid>($"SELECT id FROM catalog.invitations WHERE tenant_id = '{tenantId}'");
        await Handlers.CancelInvitationAsync(database.Services, tenantId, new CancelInvitation(tenantId, invitationId));
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);

        var accepted = await Handlers.AcceptAsync(database.Services, code, invitee);

        accepted.Error.Code.ShouldBe("invitation.not_found");
        (await FindMembershipAsync(tenantId, invitee)).ShouldBeNull();
    }

    // The first owner is invited the way onboarding does it: the email carries our accept link.
    private async Task<(Guid TenantId, string Email, string Code)> InviteOwnerAsync(IServiceProvider? services = null)
    {
        var email = Unique.Email();
        var (tenantId, _, ready) = await Handlers.OnboardAsync(services ?? database.Services, email);

        return (tenantId, email, Handlers.CodeOf(ready.Link));
    }

    private async Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMembershipAsync(tenantId, externalUserId, TestContext.Current.CancellationToken);
    }

    private Task<T> ScalarAsync<T>(string sql) => database.ScalarAsSuperuserAsync<T>(sql);
}
