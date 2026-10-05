using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Api;
using Notifications.Contracts;

namespace Notifications.IntegrationTests;

// Resend is a system we do not own; these tests pin down the requests the email channel makes (0037).
public sealed class ResendEmailChannelTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly EmailMessage Email = new("ada@example.com", "You are invited", "Open https://app.test/accept?token=abc");

    private readonly StubResend _resend = new();

    public void Dispose() => _resend.Dispose();

    [Fact]
    public async Task An_email_is_sent_through_resend_from_the_configured_address()
    {
        await Notifications().SendEmailAsync(Email, Cancellation);

        var request = _resend.Requests.ShouldHaveSingleItem();
        (request.Method, request.Uri).ShouldBe((HttpMethod.Post, new Uri("https://api.resend.test/emails")));
        request.Authorization.ShouldBe("Bearer re_test_key");
        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("from").GetString().ShouldBe("App <no-reply@app.test>");
        body.GetProperty("to").EnumerateArray().Select(to => to.GetString()).ShouldBe(["ada@example.com"]);
        body.GetProperty("subject").GetString().ShouldBe("You are invited");
        body.GetProperty("text").GetString().ShouldBe("Open https://app.test/accept?token=abc");
    }

    // A send is retried after a transient failure; the idempotency key stays the same, so Resend sends the email once (0041).
    [Fact]
    public async Task A_transient_failure_is_tried_again_with_the_same_idempotency_key()
    {
        _resend.RespondOnce(HttpStatusCode.ServiceUnavailable);

        await Notifications().SendEmailAsync(Email, Cancellation);

        _resend.Requests.Count.ShouldBe(2);
        _resend.Requests[0].IdempotencyKey.ShouldNotBeNullOrEmpty();
        _resend.Requests[1].IdempotencyKey.ShouldBe(_resend.Requests[0].IdempotencyKey);
    }

    [Fact]
    public async Task Separate_emails_have_separate_idempotency_keys()
    {
        var notifications = Notifications();

        await notifications.SendEmailAsync(Email, Cancellation);
        await notifications.SendEmailAsync(Email, Cancellation);

        _resend.Requests[1].IdempotencyKey.ShouldNotBe(_resend.Requests[0].IdempotencyKey);
    }

    [Fact]
    public async Task An_email_resend_refuses_is_an_error()
    {
        _resend.Respond(HttpStatusCode.UnprocessableEntity);

        var send = async () => await Notifications().SendEmailAsync(Email, Cancellation);

        await send.ShouldThrowAsync<HttpRequestException>();
        _resend.Requests.ShouldHaveSingleItem();
    }

    // The module as the host registers it, outside Development, with Resend replaced at the HTTP boundary.
    private INotificationsModule Notifications()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resend:ApiKey"] = "re_test_key",
                ["Resend:From"] = "App <no-reply@app.test>",
                ["Resend:ApiUrl"] = "https://api.resend.test/",
                ["Resend:RetryDelay"] = "00:00:00",
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new TestEnvironment(Environments.Production))
            .AddLogging()
            .AddNotificationsModule(configuration)
            .ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _resend))
            .BuildServiceProvider()
            .GetRequiredService<INotificationsModule>();
    }

    private sealed class StubResend : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _once = new();
        private HttpStatusCode _status = HttpStatusCode.OK;

        public List<(HttpMethod Method, Uri Uri, string? Authorization, string? IdempotencyKey, string Body)> Requests { get; } = [];

        public void Respond(HttpStatusCode status) => _status = status;

        public void RespondOnce(HttpStatusCode status) => _once.Enqueue(status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            var status = _once.TryDequeue(out var once) ? once : _status;
            return new HttpResponseMessage(status) { Content = new StringContent("""{"id":"email_1"}""", Encoding.UTF8, "application/json") };
        }
    }
}
