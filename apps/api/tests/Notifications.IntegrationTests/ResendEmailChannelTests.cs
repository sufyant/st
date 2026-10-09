using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Application;
using Notifications.Application.Ports;
using Notifications.Infrastructure;

namespace Notifications.IntegrationTests;

// Resend is a system we do not own; these tests pin down the requests the email channel makes.
public sealed class ResendEmailChannelTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly EmailMessage Email = new("ada@example.com", "You are invited", "Open https://app.test/accept?token=abc", "invite/0199a8f0-0000-7000-8000-000000000401");

    private readonly StubResend _resend = new();

    public void Dispose() => _resend.Dispose();

    [Fact]
    public async Task An_email_is_sent_through_resend_from_the_configured_address()
    {
        await Notifications().SendAsync(Email, Cancellation);

        var request = _resend.Requests.ShouldHaveSingleItem();
        (request.Method, request.Uri).ShouldBe((HttpMethod.Post, new Uri("https://api.resend.test/emails")));
        request.Authorization.ShouldBe("Bearer re_test_key");
        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("from").GetString().ShouldBe("App <no-reply@app.test>");
        body.GetProperty("to").EnumerateArray().Select(to => to.GetString()).ShouldBe(["ada@example.com"]);
        body.GetProperty("subject").GetString().ShouldBe("You are invited");
        body.GetProperty("text").GetString().ShouldBe("Open https://app.test/accept?token=abc");
    }

    // Every send to Resend has a time limit, set from configuration.
    [Fact]
    public async Task A_send_that_takes_longer_than_the_timeout_is_given_up()
    {
        _resend.Delay = TimeSpan.FromSeconds(30);

        var send = () => Notifications(timeout: "00:00:00.100").SendAsync(Email, Cancellation);

        await send.ShouldThrowAsync<TaskCanceledException>();
    }

    // Resend sends one email per idempotency key, so an email sent again under its key is not sent twice (O4).
    [Fact]
    public async Task SendEmail_SentTwice_CarriesItsOwnIdempotencyKeyBothTimes()
    {
        var channel = Notifications();

        await channel.SendAsync(Email, Cancellation);
        await channel.SendAsync(Email, Cancellation);

        _resend.Requests.Select(request => request.IdempotencyKey).ShouldBe(["invite/0199a8f0-0000-7000-8000-000000000401", "invite/0199a8f0-0000-7000-8000-000000000401"]);
    }

    [Fact]
    public async Task An_email_resend_refuses_is_an_error()
    {
        _resend.Respond(HttpStatusCode.UnprocessableEntity);

        var send = async () => await Notifications().SendAsync(Email, Cancellation);

        await send.ShouldThrowAsync<HttpRequestException>();
        _resend.Requests.ShouldHaveSingleItem();
    }

    // The module as the host registers it, outside Development, with Resend replaced at the HTTP boundary.
    private IEmailChannel Notifications(string timeout = "00:00:10")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Resend:ApiKey"] = "re_test_key",
                ["Notifications:Resend:From"] = "App <no-reply@app.test>",
                ["Notifications:Resend:ApiUrl"] = "https://api.resend.test/",
                ["Notifications:Resend:Timeout"] = timeout,
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new TestEnvironment(Environments.Production))
            .AddLogging()
            .AddNotificationsInfrastructure()
            .ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _resend))
            .BuildServiceProvider()
            .GetRequiredService<IEmailChannel>();
    }

    private sealed class StubResend : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;

        public List<(HttpMethod Method, Uri Uri, string? Authorization, string? IdempotencyKey, string Body)> Requests { get; } = [];

        public void Respond(HttpStatusCode status) => _status = status;

        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent("""{"id":"email_1"}""", Encoding.UTF8, "application/json") };
        }
    }
}
