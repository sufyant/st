using System.Net;
using System.Net.Http.Json;
using System.Text;
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
    public async Task AcceptInvitation_InvitedPerson_BecomesAMemberOfTheTenant()
    {
        var (slug, invitee, code) = await InviteSomeoneWithAnAccountAsync();

        var accepted = await AcceptAsync(invitee, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenant = await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        tenant.EnumerateObject().Select(field => field.Name).ShouldBe(["id", "name", "slug"], ignoreOrder: true);
        tenant.GetProperty("id").GetGuid().ShouldBe(await database.ScalarAsync<Guid>($"SELECT id FROM catalog.tenants WHERE slug = '{slug}'"));
        tenant.GetProperty("name").GetString().ShouldBe("Acme Ltd");
        tenant.GetProperty("slug").GetString().ShouldBe(slug);
        (await database.ScalarAsSuperuserAsync<long>(
            $"""
            SELECT count(*) FROM catalog.memberships
            JOIN catalog.tenants ON tenants.id = memberships.tenant_id JOIN catalog.users ON users.id = memberships.user_id
            WHERE tenants.slug = '{slug}' AND users.external_id = '{invitee}'
            """)).ShouldBe(1);
    }

    // The secret travels in the accept link, inside the onboarding's messages (see the deviations list); the catalog keeps only its
    // hash.
    [Fact]
    public async Task OnboardTenant_TheAcceptLinksSecret_IsStoredOnlyAsItsHash()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount($"user_{Guid.NewGuid():N}", email);
        await _api.WaitingForMessagesAsync(() => OnboardAsync(email));
        var secret = SecretOf(_api.Email.CodeSentTo(email));

        var stored = await database.ScalarAsSuperuserAsync<long>(
            $"SELECT count(*) FROM catalog.invitations WHERE email = '{email}' AND token_hash = encode(sha256(convert_to('{secret}', 'UTF8')), 'hex')");
        var holdingTheSecret = await database.ScalarAsSuperuserAsync<long>(
            $"SELECT count(*) FROM catalog.invitations WHERE position('{secret}' in invitations::text) > 0");

        stored.ShouldBe(1);
        holdingTheSecret.ShouldBe(0);
    }

    // Clerk is asked before the acceptance locks the invitation, never while its transaction holds the lock.
    [Fact]
    public async Task AcceptInvitation_AskingTheIdentityProvider_HappensBeforeTheInvitationIsLocked()
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
    public async Task AcceptInvitation_CodeUsedTwice_IsRejected()
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
    public async Task AcceptInvitation_AfterItExpires_IsAConflict()
    {
        var (_, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        _time.Advance(TimeSpan.FromDays(7));

        var accepted = await AcceptAsync(invitee, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.expired");
    }

    [Fact]
    public async Task AcceptInvitation_WithoutTheInvitedEmail_IsForbidden()
    {
        var (_, _, code) = await InviteSomeoneWithAnAccountAsync();
        var stranger = $"user_{Guid.NewGuid():N}";
        _api.Identity.AddAccount(stranger, $"{Guid.NewGuid():N}@example.com");

        var accepted = await AcceptAsync(stranger, code);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.email_mismatch");
    }

    [Fact]
    public async Task AcceptInvitation_UnknownCode_IsNotFound()
    {
        var (tenantId, _) = await _catalog.AddTenantAsync();

        var accepted = await AcceptAsync($"user_{Guid.NewGuid():N}", $"{tenantId}.no-such-secret");

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.not_found");
    }

    // A cancelled invitation answers like one that does not exist.
    [Fact]
    public async Task AcceptInvitation_Cancelled_IsNotFound()
    {
        var (_, invitee, code) = await InviteSomeoneWithAnAccountAsync();
        await database.ScalarAsSuperuserAsync<object>($"UPDATE catalog.invitations SET status = 'Cancelled' WHERE tenant_id = '{TenantIdOf(code)}'");

        var accepted = await AcceptAsync(invitee, code);

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

    // OWASP API8: a request that leaves out the code, sends it as null or sends it empty or blank is a bad request, one shape for
    // all of them, never a server error.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"code":null}""")]
    [InlineData("""{"code":""}""")]
    [InlineData("""{"code":"   "}""")]
    public async Task AcceptInvitation_WithoutACode_IsABadRequest(string body)
    {
        var accepted = await _api.CreateClient($"user_{Guid.NewGuid():N}")
            .PostAsync("/v1/invitations/accept", new StringContent(body, Encoding.UTF8, "application/json"), Cancellation);

        accepted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(accepted)).ShouldBe("invitation.code_required");
    }

    // A body the endpoint cannot read at all is a bad request in every environment, never a server error.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task AcceptInvitation_WithoutAReadableBody_IsABadRequest(string body)
    {
        var accepted = await _api.CreateClient($"user_{Guid.NewGuid():N}")
            .PostAsync("/v1/invitations/accept", new StringContent(body, Encoding.UTF8, "application/json"), Cancellation);

        accepted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        accepted.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task AcceptInvitation_WithoutASignedInUser_IsUnauthorized()
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

        return (slug, invitee, _api.Email.CodeSentTo(email));
    }

    private async Task<string> OnboardAsync(string ownerEmail)
    {
        var slug = $"tenant-{Guid.NewGuid():N}"[..20];
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);
        (await admin.CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail })).EnsureSuccessStatusCode();

        return slug;
    }

    private Task<HttpResponseMessage> AcceptAsync(string userId, string code) =>
        _api.CreateClient(userId).PostAsJsonAsync("/v1/invitations/accept", new { code }, Cancellation);

    // An invitation code is `<tenantId>.<secret>`.
    private static string TenantIdOf(string code) => code[..code.IndexOf('.', StringComparison.Ordinal)];

    private static string SecretOf(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();
}
