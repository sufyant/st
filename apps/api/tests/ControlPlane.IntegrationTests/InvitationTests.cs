using System.Security.Cryptography;
using System.Text;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SharedKernel;
using Tenancy;

namespace ControlPlane.IntegrationTests;

public sealed class InvitationTests(Database database)
{
    // The command only saves the invitation; Clerk and the email are left to the delivery, which the outbox runs once it commits.
    [Fact]
    public async Task Inviting_saves_the_invitation_and_leaves_its_delivery_to_a_message()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var email = Unique.Email();

        var (invitation, delivery) = await Handlers.InviteAsync(database.Services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);

        delivery.ShouldBe(new DeliverInvitation(invitation.Value.Id));
        database.Identity.Invitations.ShouldNotContain(invited => invited.Email == email);
        database.Sender.Sent.ShouldNotContain(sent => sent.Email == email);
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE id = '{invitation.Value.Id}' AND token_hash IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Delivering_to_someone_without_an_account_creates_a_provider_invitation_that_carries_ours()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var email = Unique.Email();

        var invitation = await Handlers.InviteAndDeliverAsync(database.Services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);

        var invited = database.Identity.Invitations.Where(invited => invited.Email == email).ShouldHaveSingleItem();
        invited.InvitationId.ShouldBe(invitation.Id);
        invited.AcceptLink.GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl);
        database.Sender.LinkSentTo(email).ShouldBe(new Uri($"https://clerk.test/invitations/{invitation.Id}"));
    }

    [Fact]
    public async Task Delivering_to_someone_with_an_account_sends_our_accept_link_only()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);

        await Handlers.InviteAndDeliverAsync(database.Services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);

        database.Identity.Invitations.ShouldNotContain(invited => invited.Email == email);
        database.Sender.LinkSentTo(email).GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl);
    }

    // The token is born in the delivery and only its SHA-256 hash is stored (0029).
    [Fact]
    public async Task Delivery_stores_the_hash_of_the_token_it_sends()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);

        var invitation = await Handlers.InviteAndDeliverAsync(database.Services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);

        var token = Handlers.TokenOf(database.Sender.LinkSentTo(email));
        (await ScalarAsync<string>($"SELECT token_hash FROM catalog.invitations WHERE id = '{invitation.Id}'")).ShouldBe(Sha256(token));
    }

    // A message may arrive twice (0024); the second delivery must not send another link.
    [Fact]
    public async Task An_invitation_is_delivered_only_once()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);
        var (_, delivery) = await Handlers.InviteAsync(database.Services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);
        await Handlers.DeliverAsync(database.Services, tenant.Id, delivery.ShouldNotBeNull());

        await Handlers.DeliverAsync(database.Services, tenant.Id, delivery);

        database.Sender.Sent.Where(sent => sent.Email == email).ShouldHaveSingleItem();
    }

    // The hash is written in the delivery's transaction, so a link that was never sent leaves no token behind.
    [Fact]
    public async Task When_sending_fails_no_token_is_kept()
    {
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(new FailingInvitationSender())));
        var tenant = await Catalog.AddTenantAsync(services);
        var owner = await Catalog.AddMemberAsync(services, tenant, BuiltInRoles.Owner);
        var (invitation, delivery) = await Handlers.InviteAsync(services, tenant.Id, owner.ExternalId, Unique.Email(), BuiltInRoles.Member.Id);

        var deliver = () => Handlers.DeliverAsync(services, tenant.Id, delivery.ShouldNotBeNull());

        await deliver.ShouldThrowAsync<InvalidOperationException>();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE id = '{invitation.Value.Id}' AND token_hash IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Accepting_creates_the_user_and_the_membership_with_the_invited_role()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var (email, token) = await InviteAsync(tenant.Id, owner.ExternalId, BuiltInRoles.Admin);
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);

        var accepted = await Handlers.AcceptAsync(database.Services, token, invitee);

        accepted.Value.TenantSlug.ShouldBe(tenant.Slug);
        (await FindMembershipAsync(tenant.Slug, invitee)).ShouldNotBeNull().Permissions.ShouldBe(BuiltInRoles.Admin.Permissions, ignoreOrder: true);
    }

    [Fact]
    public async Task An_existing_user_accepting_an_invitation_joins_another_tenant()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var other = await Catalog.AddTenantAsync(database.Services);
        var invitee = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, invitee);
        var (email, token) = await InviteAsync(tenant.Id, owner.ExternalId, BuiltInRoles.Member);
        database.Identity.AddAccount(invitee.ExternalId, email);

        await Handlers.AcceptAsync(database.Services, token, invitee.ExternalId);

        (await FindMembershipAsync(tenant.Slug, invitee.ExternalId)).ShouldNotBeNull();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{invitee.ExternalId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_acceptance_leaves_no_user_behind()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var (_, token) = await InviteAsync(tenant.Id, owner.ExternalId, BuiltInRoles.Member);
        var stranger = Unique.ExternalId();
        database.Identity.AddAccount(stranger, Unique.Email());

        var accepted = await Handlers.AcceptAsync(database.Services, token, stranger);

        accepted.Error.Code.ShouldBe("invitation.email_mismatch");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{stranger}'")).ShouldBe(0);
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_twice()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var (email, token) = await InviteAsync(tenant.Id, owner.ExternalId, BuiltInRoles.Member);
        var first = Unique.ExternalId();
        var second = Unique.ExternalId();
        database.Identity.AddAccount(first, email);
        database.Identity.AddAccount(second, email);
        await Handlers.AcceptAsync(database.Services, token, first);

        var reused = await Handlers.AcceptAsync(database.Services, token, second);

        reused.Error.Code.ShouldBe("invitation.not_pending");
        (await FindMembershipAsync(tenant.Slug, second)).ShouldBeNull();
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_after_the_invitation_expires()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time)));
        var tenant = await Catalog.AddTenantAsync(services);
        var owner = await Catalog.AddMemberAsync(services, tenant, BuiltInRoles.Owner);
        var (email, token) = await InviteAsync(tenant.Id, owner.ExternalId, BuiltInRoles.Member, services);
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);
        time.Advance(TimeSpan.FromDays(7));

        var accepted = await Handlers.AcceptAsync(services, token, invitee);

        accepted.Error.Code.ShouldBe("invitation.expired");
        (await FindMembershipAsync(tenant.Slug, invitee)).ShouldBeNull();
    }

    [Fact]
    public async Task An_admin_cannot_invite_an_owner()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var admin = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Admin);

        var (invitation, delivery) = await Handlers.InviteAsync(database.Services, tenant.Id, admin.ExternalId, Unique.Email(), BuiltInRoles.Owner.Id);

        invitation.Error.Code.ShouldBe("role.beyond_your_permissions");
        delivery.ShouldBeNull();
    }

    private async Task<(string Email, string Token)> InviteAsync(Guid tenantId, string actorId, Role role, IServiceProvider? services = null)
    {
        var email = Unique.Email();
        await Handlers.InviteAndDeliverAsync(services ?? database.Services, tenantId, actorId, email, role.Id);

        return (email, Handlers.TokenOf(database.Identity.Invitations.Single(invited => invited.Email == email).AcceptLink));
    }

    private async Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMembershipAsync(slug, externalUserId, TestContext.Current.CancellationToken);
    }

    private static string Sha256(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(DatabaseRoles.Application));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
