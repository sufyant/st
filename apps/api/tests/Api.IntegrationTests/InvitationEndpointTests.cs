using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using SharedKernel;
using Tenancy;

namespace Api.IntegrationTests;

// People join a tenant only through an invitation (0029).
public sealed class InvitationEndpointTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application), time: _time);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task An_invited_person_accepts_and_can_then_act_in_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var (invitee, token) = await InviteSomeoneWithAnAccountAsync(tenant.Slug, owner, "Admin");

        var accepted = await AcceptAsync(invitee, token);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("tenantSlug").GetString().ShouldBe(tenant.Slug);
        var invites = await InviteAsync(tenant.Slug, invitee, $"{Guid.NewGuid():N}@example.com", "Member");
        invites.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Property level: the invitation response carries no token and no hash; the token only travels in the link sent by email.
    [Fact]
    public async Task The_invitation_response_reveals_no_token()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await InviteAsync(tenant.Slug, owner, $"{Guid.NewGuid():N}@example.com", "Member");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["id", "email", "roleId", "status", "expiresAt"], ignoreOrder: true);
    }

    // The token travels only in the link the delivery sends; the stored messages that carried the invitation never held it (0029).
    [Fact]
    public async Task No_stored_message_holds_an_invitation_token()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount($"user_{Guid.NewGuid():N}", email);
        var invited = await _api.WaitingForMessagesAsync(() => InviteAsync(tenant.Slug, owner, email, "Member"));
        var invitationId = (await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetString()!;

        var holdingTheInvitation = await StoredMessagesHoldingAsync(invitationId);
        var holdingTheToken = await StoredMessagesHoldingAsync(_api.Sender.TokenSentTo(email));

        holdingTheInvitation.ShouldBeGreaterThan(0);
        holdingTheToken.ShouldBe(0);
    }

    // Clerk is asked before the acceptance locks the invitation, never while its transaction holds the lock.
    [Fact]
    public async Task Accepting_asks_the_identity_provider_before_locking_the_invitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var (invitee, token) = await InviteSomeoneWithAnAccountAsync(tenant.Slug, owner, "Member");
        var lockedWhileAsked = false;
        _api.Identity.WhileReadingVerifiedEmails = async () =>
            lockedWhileAsked = await database.IsLockedAsync($"SELECT 1 FROM catalog.invitations WHERE tenant_id = '{tenant.Id}' FOR UPDATE NOWAIT");

        var accepted = await AcceptAsync(invitee, token);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        lockedWhileAsked.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_twice()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var (invitee, token) = await InviteSomeoneWithAnAccountAsync(tenant.Slug, owner, "Member");
        var sameEmail = $"user_{Guid.NewGuid():N}";
        _api.Identity.AddAccount(sameEmail, (await _api.Identity.FindVerifiedEmailsAsync(invitee, Cancellation))[0]);
        await AcceptAsync(invitee, token);

        var reused = await AcceptAsync(sameEmail, token);

        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(reused)).ShouldBe("invitation.not_pending");
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_after_the_invitation_expires()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var (invitee, token) = await InviteSomeoneWithAnAccountAsync(tenant.Slug, owner, "Member");
        _time.Advance(TimeSpan.FromDays(7));

        var accepted = await AcceptAsync(invitee, token);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.expired");
    }

    [Fact]
    public async Task A_person_without_the_invited_email_cannot_accept()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var (_, token) = await InviteSomeoneWithAnAccountAsync(tenant.Slug, owner, "Member");
        var stranger = $"user_{Guid.NewGuid():N}";
        _api.Identity.AddAccount(stranger, $"{Guid.NewGuid():N}@example.com");

        var accepted = await AcceptAsync(stranger, token);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.email_mismatch");
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
    {
        var accepted = await AcceptAsync($"user_{Guid.NewGuid():N}", "no-such-token");

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.not_found");
    }

    [Fact]
    public async Task Accepting_needs_a_signed_in_user()
    {
        var accepted = await _api.CreateClient().PostAsJsonAsync("/v1/invitations/accept", new { token = "any" }, Cancellation);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_without_the_permission_to_invite_is_forbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var viewer = await _catalog.AddMemberAsync(tenant.Id, role: "Viewer");

        var response = await InviteAsync(tenant.Slug, viewer, $"{Guid.NewGuid():N}@example.com", "Viewer");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_cannot_invite_an_owner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");

        var response = await InviteAsync(tenant.Slug, admin, $"{Guid.NewGuid():N}@example.com", "Owner");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(response)).ShouldBe("role.beyond_your_permissions");
    }

    [Fact]
    public async Task An_invitation_needs_a_valid_email_address()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await InviteAsync(tenant.Slug, owner, "not an email", "Member");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation))!.Errors.Keys.ShouldBe(["Email"]);
    }

    [Fact]
    public async Task A_custom_role_with_the_permission_to_invite_can_invite()
    {
        var tenant = await _catalog.AddTenantAsync();
        var recruiter = await _catalog.AddCustomRoleAsync(tenant.Id, Permissions.MembersInvite);
        var member = await _catalog.AddMemberWithRoleAsync(tenant.Id, recruiter);

        var response = await InviteAsync(tenant.Slug, member, $"{Guid.NewGuid():N}@example.com", "Member");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(string Invitee, string Token)> InviteSomeoneWithAnAccountAsync(string slug, string inviter, string builtInRole)
    {
        var invitee = $"user_{Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount(invitee, email);
        (await _api.WaitingForMessagesAsync(() => InviteAsync(slug, inviter, email, builtInRole))).EnsureSuccessStatusCode();

        return (invitee, _api.Sender.TokenSentTo(email));
    }

    private async Task<HttpResponseMessage> InviteAsync(string slug, string inviter, string email, string builtInRole)
    {
        var roleId = await database.ScalarAsync<Guid>($"SELECT id FROM catalog.roles WHERE built_in = '{builtInRole}'");
        return await _api.CreateClient(inviter).PostAsJsonAsync($"/v1/tenants/{slug}/invitations", new { email, roleId }, Cancellation);
    }

    private Task<HttpResponseMessage> AcceptAsync(string userId, string token) =>
        _api.CreateClient(userId).PostAsJsonAsync("/v1/invitations/accept", new { token }, Cancellation);

    private Task<long> StoredMessagesHoldingAsync(string text) =>
        database.ScalarAsync<long>(
            $"""
            SELECT (SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE position(convert_to('{text}', 'UTF8') in body) > 0)
                 + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE position(convert_to('{text}', 'UTF8') in body) > 0)
                 + (SELECT count(*) FROM wolverine.wolverine_dead_letters WHERE position(convert_to('{text}', 'UTF8') in body) > 0)
            """);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();
}
