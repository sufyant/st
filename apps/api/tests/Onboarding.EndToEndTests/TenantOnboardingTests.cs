using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Onboarding.EndToEndTests;

// The tracer bullet: the whole onboarding flow through HTTP against the composed application. A system admin creates a
// tenant, and its first owner signs up through the invitation and accepts it.
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
    public async Task A_system_admin_onboards_a_tenant_whose_first_owner_accepts()
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

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        _app.Sender.Sent.Single(sent => sent.Email == ownerEmail).Link.ShouldBe(new Uri($"https://clerk.test/invitations/{invitation.InvitationId}"));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("tenantSlug").GetString().ShouldBe(slug);
    }

    private static string TokenOf(Uri acceptLink) =>
        Uri.UnescapeDataString(acceptLink.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("token=", StringComparison.Ordinal))["token=".Length..]);
}
