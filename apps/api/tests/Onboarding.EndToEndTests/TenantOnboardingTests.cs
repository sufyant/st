using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Onboarding.EndToEndTests;

// The tracer bullet: the whole onboarding flow through HTTP against the composed application, as it is deployed: one build output
// running as a web host and a worker host on one database (section 1). The first system admin, named by configuration, creates a
// tenant; its first owner gets the email, signs up with the identity provider like any other user, accepts the invitation with the
// code from the email's link and sees the tenant; another user does not find it.
public sealed class TenantOnboardingTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly FakeIdentityProvider _identity = new();
    private readonly FakeEmailChannel _email = new();
    private OnboardingApp _web = null!;
    private OnboardingApp _worker = null!;

    public ValueTask InitializeAsync()
    {
        _web = new OnboardingApp(database, "web", _identity, _email);
        _worker = new OnboardingApp(database, "worker", _identity, _email);
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _web.DisposeAsync();
    }

    [Fact]
    public async Task OnboardTenant_FirstOwnerAccepts_OwnerSeesTheTenantAndAStrangerGetsNotFound()
    {
        var admin = _web.ClientFor(Database.SystemAdmin, secondFactor: true);
        var owner = _web.ClientFor("user_e2e_owner");
        var stranger = _web.ClientFor("user_e2e_stranger");
        const string slug = "acme";
        const string ownerEmail = "owner@acme.test";
        _identity.SignUp(Database.SystemAdmin, OnboardingApp.FirstSystemAdminEmail);

        var created = await admin.SendAsync(CreateTenant(new { name = "Acme Ltd", slug, ownerEmail }), Cancellation);
        var tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
        await Waiting.UntilAsync(() => _email.Sent.Any(sent => sent.To == ownerEmail));
        var acceptLink = LinkIn(_email.Sent.Single(sent => sent.To == ownerEmail).Text);
        _identity.SignUp("user_e2e_owner", ownerEmail);
        var accepted = await owner.PostAsJsonAsync("/v1/invitations/accept", new { code = CodeOf(acceptLink) }, Cancellation);
        var myTenants = await owner.GetFromJsonAsync<JsonElement>("/v1/me/tenants", Cancellation);
        var members = await owner.GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenantId}/members", Cancellation);
        var strangerMembers = await stranger.GetAsync($"/v1/tenants/{tenantId}/members", Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        acceptLink.GetLeftPart(UriPartial.Path).ShouldBe("https://app.test/invitations/accept");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid().ShouldBe(tenantId);
        myTenants.GetProperty("items").EnumerateArray()
            .Select(tenant => (tenant.GetProperty("id").GetGuid(), tenant.GetProperty("name").GetString(), tenant.GetProperty("slug").GetString()))
            .ShouldBe([(tenantId, "Acme Ltd", slug)]);
        members.GetProperty("items").EnumerateArray().Select(member => member.GetProperty("role").GetString()).ShouldBe(["Owner"]);
        strangerMembers.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // The client gives each tenant it creates a key, so a repeated request does not create a second tenant.
    private static HttpRequestMessage CreateTenant(object body) => new(HttpMethod.Post, "/v1/system/tenants")
    {
        Content = JsonContent.Create(body),
        Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } },
    };

    // The one link in the email's text.
    private static Uri LinkIn(string text) => new(text.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Single(word => word.StartsWith("https://", StringComparison.Ordinal)));

    private static string CodeOf(Uri acceptLink) =>
        Uri.UnescapeDataString(acceptLink.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("code=", StringComparison.Ordinal))["code=".Length..]);
}
