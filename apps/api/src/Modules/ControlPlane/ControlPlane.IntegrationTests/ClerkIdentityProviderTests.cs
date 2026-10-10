using System.Net;
using System.Text;
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
    public async Task CallClerk_AnyRequest_IsAuthorizedWithTheSecretKey()
    {
        _clerk.Respond("""{"id":"user_1","email_addresses":[]}""");

        await Identity().FindVerifiedEmailsAsync("user_1", Cancellation);

        _clerk.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer sk_test_secret");
    }

    [Fact]
    public async Task FindVerifiedEmails_SomeUnverified_ReturnsOnlyTheVerified()
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
    public async Task CallClerk_KeepsFailing_IsAnError()
    {
        _clerk.Respond("""{"errors":[{"code":"internal"}]}""", HttpStatusCode.InternalServerError);

        var find = async () => await Identity().FindVerifiedEmailsAsync("user_1", Cancellation);

        await find.ShouldThrowAsync<HttpRequestException>();
    }

    // Every call to Clerk has a time limit, set from configuration.
    [Fact]
    public async Task CallClerk_SlowerThanTheTimeout_IsGivenUp()
    {
        _clerk.Delay = TimeSpan.FromSeconds(30);

        var find = async () => await Identity(timeout: "00:00:00.100").FindVerifiedEmailsAsync("user_1", Cancellation);

        await find.ShouldThrowAsync<TaskCanceledException>();
    }

    // The module's real adapter, as the host registers it, with Clerk replaced at the HTTP boundary.
    private IIdentityProvider Identity(string timeout = "00:00:10")
    {
        var services = database.BuildServices(
            services => services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _clerk)),
            settings: new()
            {
                ["ControlPlane:Clerk:SecretKey"] = "sk_test_secret",
                ["ControlPlane:Clerk:BackendApiUrl"] = "https://api.clerk.test/v1/",
                ["ControlPlane:Clerk:Timeout"] = timeout,
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
