using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharedKernel;

namespace Onboarding.EndToEndTests;

// The tracer bullet (0042): the whole onboarding flow through HTTP against the composed application. A system admin creates a
// tenant, its first owner signs up through the invitation and accepts it, and the owner can then act inside the tenant.
public sealed class TenantOnboardingTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private OnboardingApp _app = null!;

    public ValueTask InitializeAsync()
    {
        _app = new OnboardingApp(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task A_system_admin_onboards_a_tenant_whose_first_owner_accepts_and_then_acts_in_it()
    {
        var admin = _app.ClientFor(Database.SystemAdmin, secondFactor: true);
        var owner = _app.ClientFor("user_e2e_owner");
        const string slug = "acme";
        const string ownerEmail = "owner@acme.test";

        var created = await _app.WaitingForMessagesAsync(() =>
            admin.PostAsJsonAsync("/v1/admin/tenants", new { slug, ownerEmail }, Cancellation));
        var invitation = _app.Identity.Invitations.Single(invited => invited.Email == ownerEmail);
        _app.Identity.SignUp("user_e2e_owner", ownerEmail);
        var accepted = await owner.PostAsJsonAsync("/v1/invitations/accept", new { token = TokenOf(invitation.AcceptLink) }, Cancellation);
        var role = await owner.PostAsJsonAsync($"/v1/tenants/{slug}/roles", new { name = "Support", permissions = new[] { Permissions.MembersInvite } }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        _app.Sender.Sent.Single(sent => sent.Email == ownerEmail).Link.ShouldBe(new Uri($"https://clerk.test/invitations/{invitation.InvitationId}"));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("tenantSlug").GetString().ShouldBe(slug);
        role.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static string TokenOf(Uri acceptLink) =>
        Uri.UnescapeDataString(acceptLink.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("token=", StringComparison.Ordinal))["token=".Length..]);
}
