using System.Security.Cryptography;
using System.Text;
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
    public async Task Delivering_to_someone_without_an_account_creates_a_provider_invitation_that_carries_ours()
    {
        var email = Unique.Email();

        var (_, _, invitationId) = await Handlers.InviteAndDeliverAsync(database.Services, email);

        var invited = database.Identity.Invitations.Where(invited => invited.Email == email).ShouldHaveSingleItem();
        invited.InvitationId.ShouldBe(invitationId);
        invited.AcceptLink.GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl);
        database.Sender.LinkSentTo(email).ShouldBe(new Uri($"https://clerk.test/invitations/{invitationId}"));
    }

    // The link carries one invitation code, `<tenantId>.<secret>`, also when the provider's sign-up leads to it.
    [Fact]
    public async Task DeliverInvitation_ToSomeoneWithoutAnAccount_LinksThroughTheProviderToTheInvitationCode()
    {
        var email = Unique.Email();

        var (tenantId, _, _) = await Handlers.InviteAndDeliverAsync(database.Services, email);

        var code = Handlers.CodeOf(database.Identity.Invitations.Single(invited => invited.Email == email).AcceptLink);
        Handlers.TenantIdOf(code).ShouldBe(tenantId);
        Handlers.SecretOf(code).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task DeliverInvitation_ToSomeoneWithAnAccount_SendsTheInvitationCode()
    {
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);

        var (tenantId, _, _) = await Handlers.InviteAndDeliverAsync(database.Services, email);

        var code = Handlers.CodeOf(database.Sender.LinkSentTo(email));
        Handlers.TenantIdOf(code).ShouldBe(tenantId);
        Handlers.SecretOf(code).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Delivering_to_someone_with_an_account_sends_our_accept_link_only()
    {
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);

        await Handlers.InviteAndDeliverAsync(database.Services, email);

        database.Identity.Invitations.ShouldNotContain(invited => invited.Email == email);
        database.Sender.LinkSentTo(email).GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl);
    }

    // The secret is born in the delivery and only its SHA-256 hash is stored.
    [Fact]
    public async Task Delivery_stores_the_hash_of_the_token_it_sends()
    {
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);

        var (_, _, invitationId) = await Handlers.InviteAndDeliverAsync(database.Services, email);

        var secret = Handlers.SecretOf(Handlers.CodeOf(database.Sender.LinkSentTo(email)));
        (await ScalarAsync<string>($"SELECT token_hash FROM catalog.invitations WHERE id = '{invitationId}'")).ShouldBe(Sha256(secret));
    }

    // A message may arrive twice; the second delivery must not send another link.
    [Fact]
    public async Task An_invitation_is_delivered_only_once()
    {
        var email = Unique.Email();
        database.Identity.AddAccount(Unique.ExternalId(), email);
        var (tenantId, _, _, delivery) = await Handlers.InviteFirstOwnerAsync(database.Services, email);
        await Handlers.DeliverAsync(database.Services, tenantId, delivery);

        await Handlers.DeliverAsync(database.Services, tenantId, delivery);

        database.Sender.Sent.Where(sent => sent.Email == email).ShouldHaveSingleItem();
    }

    // The hash is written in the delivery's transaction, so a link that was never sent leaves no token behind.
    [Fact]
    public async Task When_sending_fails_no_token_is_kept()
    {
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(new FailingInvitationSender())));
        var (tenantId, _, invitationId, delivery) = await Handlers.InviteFirstOwnerAsync(services, Unique.Email());

        var deliver = () => Handlers.DeliverAsync(services, tenantId, delivery);

        await deliver.ShouldThrowAsync<InvalidOperationException>();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE id = '{invitationId}' AND token_hash IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Accepting_creates_the_user_and_the_membership_with_the_invited_role()
    {
        var (tenantId, email, code) = await InviteAsync();
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
    public async Task An_existing_user_accepting_an_invitation_joins_another_tenant()
    {
        var other = await Catalog.AddTenantAsync(database.Services);
        var invitee = await Catalog.AddUserAsync(database.Services);
        await Catalog.AddMemberAsync(database.Services, other, invitee);
        var (tenantId, email, code) = await InviteAsync();
        database.Identity.AddAccount(invitee.ExternalId, email);

        await Handlers.AcceptAsync(database.Services, code, invitee.ExternalId);

        (await FindMembershipAsync(tenantId, invitee.ExternalId)).ShouldNotBeNull();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{invitee.ExternalId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_acceptance_leaves_no_user_behind()
    {
        var (_, _, code) = await InviteAsync();
        var stranger = Unique.ExternalId();
        database.Identity.AddAccount(stranger, Unique.Email());

        var accepted = await Handlers.AcceptAsync(database.Services, code, stranger);

        accepted.Error.Code.ShouldBe("invitation.email_mismatch");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.users WHERE external_id = '{stranger}'")).ShouldBe(0);
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_twice()
    {
        var (tenantId, email, code) = await InviteAsync();
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
    public async Task An_invitation_token_cannot_be_used_after_the_invitation_expires()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time)));
        var (tenantId, email, code) = await InviteAsync(services);
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);
        time.Advance(TimeSpan.FromDays(7));

        var accepted = await Handlers.AcceptAsync(services, code, invitee);

        accepted.Error.Code.ShouldBe("invitation.expired");
        (await FindMembershipAsync(tenantId, invitee)).ShouldBeNull();
    }

    private async Task<(Guid TenantId, string Email, string Code)> InviteAsync(IServiceProvider? services = null)
    {
        var email = Unique.Email();
        var (tenantId, _, _) = await Handlers.InviteAndDeliverAsync(services ?? database.Services, email);

        return (tenantId, email, Handlers.CodeOf(database.Identity.Invitations.Single(invited => invited.Email == email).AcceptLink));
    }

    private async Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string externalUserId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
            .FindMembershipAsync(tenantId, externalUserId, TestContext.Current.CancellationToken);
    }

    private static string Sha256(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private Task<T> ScalarAsync<T>(string sql) => database.ScalarAsSuperuserAsync<T>(sql);
}
