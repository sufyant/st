using System.Net;
using System.Text;
using System.Text.Json;
using ControlPlane.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.IntegrationTests;

// Clerk's Backend API is a system we do not own; these tests pin down the requests the adapter makes and how it reads the answers.
public sealed class ClerkIdentityProviderTests(Database database) : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly StubClerk _clerk = new();

    public void Dispose() => _clerk.Dispose();

    [Fact]
    public async Task Requests_are_authorized_with_the_secret_key()
    {
        _clerk.Respond("[]");

        await Identity().HasAccountAsync("ada@example.com", Cancellation);

        _clerk.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer sk_test_secret");
    }

    [Fact]
    public async Task An_account_exists_when_a_user_owns_the_email()
    {
        _clerk.Respond("""[{"id":"user_1","email_addresses":[{"email_address":"Ada@Example.com","verification":{"status":"verified"}}]}]""");

        var hasAccount = await Identity().HasAccountAsync("ada@example.com", Cancellation);

        hasAccount.ShouldBeTrue();
        _clerk.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://api.clerk.test/v1/users?email_address=ada%40example.com"));
    }

    [Fact]
    public async Task No_account_exists_when_no_user_owns_the_email()
    {
        _clerk.Respond("[]");

        var hasAccount = await Identity().HasAccountAsync("ada@example.com", Cancellation);

        hasAccount.ShouldBeFalse();
    }

    // Clerk matches some email filters partially; only a user who owns exactly this address counts.
    [Fact]
    public async Task A_user_whose_email_only_resembles_it_is_not_an_account()
    {
        _clerk.Respond("""[{"id":"user_1","email_addresses":[{"email_address":"bada@example.com","verification":{"status":"verified"}}]}]""");

        var hasAccount = await Identity().HasAccountAsync("ada@example.com", Cancellation);

        hasAccount.ShouldBeFalse();
    }

    [Fact]
    public async Task An_invitation_is_created_without_a_clerk_email_and_carries_our_invitation_id()
    {
        var invitationId = new Guid("0199a8f0-0000-7000-8000-000000000401");
        _clerk.Respond("""{"object":"invitation","id":"inv_1","url":"https://accounts.clerk.test/sign-up?__clerk_ticket=t"}""");

        var link = await Identity().InviteAsync(
            "ada@example.com", invitationId, new Uri("https://app.test/invitations/accept?token=abc"), Cancellation);

        link.ShouldBe(new Uri("https://accounts.clerk.test/sign-up?__clerk_ticket=t"));
        var request = _clerk.Requests.ShouldHaveSingleItem();
        (request.Method, request.Uri).ShouldBe((HttpMethod.Post, new Uri("https://api.clerk.test/v1/invitations")));
        var body = JsonDocument.Parse(request.Body!).RootElement;
        body.GetProperty("email_address").GetString().ShouldBe("ada@example.com");
        body.GetProperty("notify").GetBoolean().ShouldBeFalse();
        body.GetProperty("ignore_existing").GetBoolean().ShouldBeTrue();
        body.GetProperty("redirect_url").GetString().ShouldBe("https://app.test/invitations/accept?token=abc");
        body.GetProperty("public_metadata").GetProperty("invitation_id").GetString().ShouldBe(invitationId.ToString());
    }

    [Fact]
    public async Task Only_verified_email_addresses_count()
    {
        _clerk.Respond("""
            {"id":"user_1","email_addresses":[
              {"email_address":"ada@example.com","verification":{"status":"verified"}},
              {"email_address":"ada@work.example","verification":{"status":"unverified"}},
              {"email_address":"ada@old.example","verification":null}]}
            """);

        var emails = await Identity().FindVerifiedEmailsAsync("user_1", Cancellation);

        emails.ShouldBe(["ada@example.com"]);
        _clerk.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://api.clerk.test/v1/users/user_1"));
    }

    [Fact]
    public async Task A_request_that_keeps_failing_is_an_error()
    {
        _clerk.Respond("""{"errors":[{"code":"internal"}]}""", HttpStatusCode.InternalServerError);

        var hasAccount = async () => await Identity().HasAccountAsync("ada@example.com", Cancellation);

        await hasAccount.ShouldThrowAsync<HttpRequestException>();
    }

    // Every call to Clerk has a time limit, set from configuration.
    [Fact]
    public async Task A_call_that_takes_longer_than_the_timeout_is_given_up()
    {
        _clerk.Delay = TimeSpan.FromSeconds(30);

        var hasAccount = async () => await Identity(timeout: "00:00:00.100").HasAccountAsync("ada@example.com", Cancellation);

        await hasAccount.ShouldThrowAsync<TaskCanceledException>();
    }

    // The module's real adapter, as the host registers it, with Clerk replaced at the HTTP boundary.
    private IIdentityProvider Identity(string timeout = "00:00:10")
    {
        var services = database.BuildServices(
            services => services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _clerk)),
            settings: new()
            {
                ["Clerk:SecretKey"] = "sk_test_secret",
                ["Clerk:BackendApiUrl"] = "https://api.clerk.test/v1/",
                ["Clerk:Timeout"] = timeout,
            },
            realIdentityProvider: true);

        return services.GetRequiredService<IIdentityProvider>();
    }

    private sealed class StubClerk : HttpMessageHandler
    {
        private string _body = "{}";
        private HttpStatusCode _status = HttpStatusCode.OK;

        public List<(HttpMethod Method, Uri Uri, string? Authorization, string? Body)> Requests { get; } = [];

        public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) => (_body, _status) = (body, status);

        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
