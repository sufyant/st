using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tenancy;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// The invitation email goes through the Notifications module: through Resend, or to the log in Development without
// Resend. Delivery is a message the outbox sends once the invitation is saved.
public sealed class InvitationDeliveryTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);

    [Fact]
    public async Task In_development_the_invitation_link_is_written_to_the_log()
    {
        await using var api = Api(Environments.Development);
        var email = $"{Guid.NewGuid():N}@example.com";

        var response = await api.WaitingForMessagesAsync(() => InviteAsync(api, email));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record => record.Message.Contains(email, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outside_development_the_invitation_email_is_sent_through_resend()
    {
        using var resend = new StubResend();
        await using var api = Api(Environments.Production, resend, Resend);
        var email = $"{Guid.NewGuid():N}@example.com";

        var response = await api.WaitingForMessagesAsync(() => InviteAsync(api, email));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sent = resend.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://api.resend.test/emails"));
        var body = JsonDocument.Parse(sent.Body).RootElement;
        body.GetProperty("to").EnumerateArray().Single().GetString().ShouldBe(email);
        body.GetProperty("text").GetString()!.ShouldContain(api.Identity.Invitations.Single(invitation => invitation.Email == email).InvitationId.ToString());
    }

    // An invitation nobody can receive must not look sent: outside Development a pod that cannot send email gets no traffic.
    [Fact]
    public async Task Outside_development_the_application_is_not_ready_without_resend()
    {
        await using var api = Api(Environments.Production);

        var ready = await api.CreateClient().GetAsync("/health/ready", Cancellation);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Clerk or the email channel may be down for a moment: a failed delivery is tried again, each time after a longer pause.
    [Fact]
    public async Task A_failed_delivery_is_tried_again_after_growing_pauses()
    {
        var sender = new FlakyInvitationSender(failures: 2);
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(sender)));
        var email = $"{Guid.NewGuid():N}@example.com";

        var messages = await api.TrackMessagesAsync(() => InviteAsync(api, email));

        var attempts = messages.ExecutionStarted.RecordsInOrder()
            .Where(record => record.Message is DeliverInvitation)
            .Select(record => record.SessionTime)
            .ToArray();
        attempts.Length.ShouldBe(3);
        (attempts[1] - attempts[0]).ShouldBeGreaterThanOrEqualTo(1000);
        (attempts[2] - attempts[1]).ShouldBeGreaterThanOrEqualTo(2000);
        sender.Sent.ShouldHaveSingleItem().Email.ShouldBe(email);
    }

    private static readonly Dictionary<string, string?> Resend = new()
    {
        ["Resend:ApiKey"] = "re_test_key",
        ["Resend:From"] = "App <no-reply@app.test>",
        ["Resend:ApiUrl"] = "https://api.resend.test/",
    };

    // The application's own invitation sender and email channel, with Resend replaced at the HTTP boundary when it is configured.
    private ApiFactory Api(string environment, StubResend? resend = null, Dictionary<string, string?>? settings = null) => new(
        database.ConnectionStringFor(DatabaseRoles.Application),
        environment: environment,
        fakeInvitationSender: false,
        settings: settings,
        configureServices: services =>
        {
            services.AddFakeLogging();
            if (resend is not null)
            {
                services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => resend));
            }
        });

    // Onboarding a tenant invites its first owner.
    private async Task<HttpResponseMessage> InviteAsync(ApiFactory api, string email)
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var slug = $"tenant-{Guid.NewGuid():N}"[..20];

        return await api.CreateClient(admin, secondFactor: true).PostAsJsonAsync("/v1/system/tenants", new { name = "Acme Ltd", slug, ownerEmail = email }, Cancellation);
    }

    private sealed class StubResend : HttpMessageHandler
    {
        public ConcurrentBag<(Uri Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"email_1"}""") };
        }
    }
}
