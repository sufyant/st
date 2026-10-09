using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using Tenancy;

namespace Api.IntegrationTests;

// People join a tenant only through an invitation.
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
    public async Task An_invited_person_accepts_and_becomes_a_member_of_the_tenant()
    {
        var (slug, invitee, code) = await InviteSomeoneWithAnAccountAsync();

        var accepted = await AcceptAsync(invitee, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("tenantSlug").GetString().ShouldBe(slug);
        (await database.ScalarAsSuperuserAsync<long>(
            $"""
            SELECT count(*) FROM catalog.memberships
            JOIN catalog.tenants ON tenants.id = memberships.tenant_id JOIN catalog.users ON users.id = memberships.user_id
            WHERE tenants.slug = '{slug}' AND users.external_id = '{invitee}'
            """)).ShouldBe(1);
    }

    // The secret travels only in the link the delivery sends; the stored messages that carried the invitation never held it.
    [Fact]
    public async Task No_stored_message_holds_an_invitation_token()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount($"user_{Guid.NewGuid():N}", email);
        await _api.WaitingForMessagesAsync(() => OnboardAsync(email));
        var invitationId = (await database.ScalarAsSuperuserAsync<Guid>($"SELECT id FROM catalog.invitations WHERE email = '{email}'")).ToString();

        var holdingTheInvitation = await StoredMessagesHoldingAsync(invitationId);
        var holdingTheSecret = await StoredMessagesHoldingAsync(SecretOf(_api.Sender.CodeSentTo(email)));

        holdingTheInvitation.ShouldBeGreaterThan(0);
        holdingTheSecret.ShouldBe(0);
    }

    // Clerk is asked before the acceptance locks the invitation, never while its transaction holds the lock.
    [Fact]
    public async Task Accepting_asks_the_identity_provider_before_locking_the_invitation()
    {
        var (slug, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        var tenantId = await database.ScalarAsync<Guid>($"SELECT id FROM catalog.tenants WHERE slug = '{slug}'");
        var lockedWhileAsked = false;
        _api.Identity.WhileReadingVerifiedEmails = async () => lockedWhileAsked = await database.IsLockedAsync(
            tenantId, $"SELECT 1 FROM catalog.invitations WHERE tenant_id = '{tenantId}' FOR UPDATE NOWAIT");

        var accepted = await AcceptAsync(invitee, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        lockedWhileAsked.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_twice()
    {
        var (_, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        var sameEmail = $"user_{Guid.NewGuid():N}";
        _api.Identity.AddAccount(sameEmail, (await _api.Identity.FindVerifiedEmailsAsync(invitee, Cancellation))[0]);
        await AcceptAsync(invitee, code);

        var reused = await AcceptAsync(sameEmail, code);

        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(reused)).ShouldBe("invitation.not_pending");
    }

    [Fact]
    public async Task An_invitation_token_cannot_be_used_after_the_invitation_expires()
    {
        var (_, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        _time.Advance(TimeSpan.FromDays(7));

        var accepted = await AcceptAsync(invitee, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.expired");
    }

    [Fact]
    public async Task A_person_without_the_invited_email_cannot_accept()
    {
        var (_, _, code) = await InviteSomeoneWithAnAccountAsync();
        var stranger = $"user_{Guid.NewGuid():N}";
        _api.Identity.AddAccount(stranger, $"{Guid.NewGuid():N}@example.com");

        var accepted = await AcceptAsync(stranger, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.email_mismatch");
    }

    [Fact]
    public async Task An_unknown_code_is_not_found()
    {
        var (tenantId, _) = await _catalog.AddTenantAsync();

        var accepted = await AcceptAsync($"user_{Guid.NewGuid():N}", $"{tenantId}.no-such-secret");

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.not_found");
    }

    // The tenant id in the code is not secret: with another tenant's id the right secret finds nothing, and the answer does not
    // tell which part was wrong.
    [Fact]
    public async Task AcceptInvitation_TheSecretWithAnotherTenantsId_IsNotFound()
    {
        var (_, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        var (otherTenantId, _) = await _catalog.AddTenantAsync();

        var accepted = await AcceptAsync(invitee, $"{otherTenantId}.{SecretOf(code)}");

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.not_found");
        (await database.ScalarAsSuperuserAsync<string>(
            $"SELECT status FROM catalog.invitations WHERE tenant_id = '{TenantIdOf(code)}'")).ShouldBe("Pending");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-dot-at-all")]
    [InlineData("not-a-uuid.k3J9xSecret")]
    [InlineData(".k3J9xSecret")]
    [InlineData("018f3a2c-7d4e-7a1b-9c2d-3e4f5a6b9b1e.")]
    public async Task AcceptInvitation_MalformedCode_IsNotFound(string code)
    {
        var accepted = await AcceptAsync($"user_{Guid.NewGuid():N}", code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.not_found");
    }

    [Fact]
    public async Task Accepting_needs_a_signed_in_user()
    {
        var accepted = await _api.CreateClient().PostAsJsonAsync("/v1/invitations/accept", new { code = "any" }, Cancellation);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Onboarding a tenant invites its first owner, here someone who already has an account, so the link carries our invitation
    // code.
    private async Task<(string Slug, string Invitee, string Code)> InviteSomeoneWithAnAccountAsync()
    {
        var invitee = $"user_{Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount(invitee, email);
        var slug = await _api.WaitingForMessagesAsync(() => OnboardAsync(email));

        return (slug, invitee, _api.Sender.CodeSentTo(email));
    }

    private async Task<string> OnboardAsync(string ownerEmail)
    {
        var slug = $"tenant-{Guid.NewGuid():N}"[..20];
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);
        (await admin.PostAsJsonAsync("/v1/system/tenants", new { name = "Acme Ltd", slug, ownerEmail }, Cancellation)).EnsureSuccessStatusCode();

        return slug;
    }

    private Task<HttpResponseMessage> AcceptAsync(string userId, string code) =>
        _api.CreateClient(userId).PostAsJsonAsync("/v1/invitations/accept", new { code }, Cancellation);

    // An invitation code is `<tenantId>.<secret>`.
    private static string TenantIdOf(string code) => code[..code.IndexOf('.', StringComparison.Ordinal)];

    private static string SecretOf(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];

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
