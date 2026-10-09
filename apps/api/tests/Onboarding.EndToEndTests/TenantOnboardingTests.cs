using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Onboarding.EndToEndTests;

// The tracer bullet: the whole onboarding flow through HTTP against the composed application. A system admin creates a
// tenant, its first owner signs up through the invitation, accepts it and sees the tenant; another user does not find it.
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
    public async Task A_system_admin_onboards_a_tenant_whose_first_owner_accepts_and_sees_it()
    {
        var admin = _app.ClientFor(Database.SystemAdmin, secondFactor: true);
        var owner = _app.ClientFor("user_e2e_owner");
        var stranger = _app.ClientFor("user_e2e_stranger");
        const string slug = "acme";
        const string ownerEmail = "owner@acme.test";

        var created = await _app.WaitingForMessagesAsync(() =>
            admin.PostAsJsonAsync("/v1/system/tenants", new { name = "Acme Ltd", slug, ownerEmail }, Cancellation));
        var tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
        var invitation = _app.Identity.Invitations.Single(invited => invited.Email == ownerEmail);
        _app.Identity.SignUp("user_e2e_owner", ownerEmail);
        var accepted = await owner.PostAsJsonAsync("/v1/invitations/accept", new { code = CodeOf(invitation.AcceptLink) }, Cancellation);
        var myTenants = await owner.GetFromJsonAsync<JsonElement>("/v1/me/tenants", Cancellation);
        var members = await owner.GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenantId}/members", Cancellation);
        var strangerMembers = await stranger.GetAsync($"/v1/tenants/{tenantId}/members", Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        _app.Email.Sent.Single(sent => sent.To == ownerEmail).Text.ShouldContain($"https://clerk.test/invitations/{invitation.InvitationId}");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid().ShouldBe(tenantId);
        myTenants.GetProperty("items").EnumerateArray()
            .Select(tenant => (tenant.GetProperty("id").GetGuid(), tenant.GetProperty("name").GetString(), tenant.GetProperty("slug").GetString()))
            .ShouldBe([(tenantId, "Acme Ltd", slug)]);
        members.GetProperty("items").EnumerateArray().Select(member => member.GetProperty("role").GetString()).ShouldBe(["Owner"]);
        strangerMembers.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string CodeOf(Uri acceptLink) =>
        Uri.UnescapeDataString(acceptLink.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("code=", StringComparison.Ordinal))["code=".Length..]);
}
