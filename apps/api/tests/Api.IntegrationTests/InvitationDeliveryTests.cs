using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tenancy;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// The invitation email goes through the Notifications module: through Resend, or to the log in Development without
// Resend. It is sent from the event ControlPlane publishes once the tenant is active.
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
        var invitationId = api.Identity.Invitations.Single(invitation => invitation.Email == email).InvitationId;
        var sent = resend.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://api.resend.test/emails"));
        sent.IdempotencyKey.ShouldBe($"invite/{invitationId}");
        var body = JsonDocument.Parse(sent.Body).RootElement;
        body.GetProperty("to").EnumerateArray().Single().GetString().ShouldBe(email);
        body.GetProperty("text").GetString()!.ShouldContain($"https://clerk.test/invitations/{invitationId}");
    }

    // An invitation nobody can receive must not look sent: outside Development a pod that cannot send email does not start.
    [Fact]
    public async Task Outside_development_the_application_does_not_start_without_resend()
    {
        await using var api = Api(Environments.Production);

        var start = () => api.CreateClient();

        start.ShouldThrow<Exception>().Message.ShouldContain("Notifications:Resend:ApiKey");
    }

    // The email service may be down for a while: a failed email is tried again, each time after a longer pause.
    [Fact]
    public async Task SendInvitation_TheEmailServiceRecovers_IsTriedAgainAfterGrowingPauses()
    {
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application),
            settings: new Dictionary<string, string?>
            {
                ["Notifications:InvitationEmailRetryDelays:0"] = "00:00:00.100",
                ["Notifications:InvitationEmailRetryDelays:1"] = "00:00:00.200",
                ["Notifications:InvitationEmailRetryDelays:2"] = "00:00:00.400",
            });
        api.Email.FailNext(2);
        var email = $"{Guid.NewGuid():N}@example.com";

        var messages = await api.TrackMessagesAsync(() => InviteAsync(api, email));

        var attempts = messages.ExecutionStarted.RecordsInOrder()
            .Where(record => record.Message is OwnerInvitationReady)
            .Select(record => record.SessionTime)
            .ToArray();
        attempts.Length.ShouldBe(3);
        (attempts[1] - attempts[0]).ShouldBeGreaterThanOrEqualTo(100);
        (attempts[2] - attempts[1]).ShouldBeGreaterThanOrEqualTo(200);
        api.Email.Sent.ShouldHaveSingleItem().To.ShouldBe(email);
    }

    // A pause of a minute or more is a scheduled retry, not a wait inside the handler, so the queue goes on with the next invitation.
    [Fact]
    public async Task SendInvitation_TheNextTryIsAMinuteAway_IsScheduledWhileTheQueueHandlesTheNextInvitation()
    {
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application),
            settings: new Dictionary<string, string?>
            {
                ["Notifications:InvitationEmailRetryDelays:0"] = "00:01:00",
                ["Notifications:InvitationEmailRetryDelays:1"] = "00:02:00",
                ["Notifications:InvitationEmailRetryDelays:2"] = "00:03:00",
            });
        var refused = $"{Guid.NewGuid():N}@example.com";
        var next = $"{Guid.NewGuid():N}@example.com";
        api.Email.Refuses = refused;

        await api.TrackMessagesAsync(async () =>
        {
            await InviteAsync(api, refused);
            await InviteAsync(api, next);
        });

        api.Email.Sent.ShouldHaveSingleItem().To.ShouldBe(next);
        (await database.ScalarAsync<long>(
                $"""
                SELECT count(*) FROM wolverine.wolverine_incoming_envelopes
                WHERE status = 'Scheduled' AND message_type LIKE '%OwnerInvitationReady' AND execution_time > now() + interval '30 seconds'
                AND position(convert_to('{refused}', 'UTF8') IN body) > 0
                """))
            .ShouldBe(1);
    }

    private static readonly Dictionary<string, string?> Resend = new()
    {
        ["Notifications:Resend:ApiKey"] = "re_test_key",
        ["Notifications:Resend:From"] = "App <no-reply@app.test>",
        ["Notifications:Resend:ApiUrl"] = "https://api.resend.test/",
    };

    // The application's own email channel, with Resend replaced at the HTTP boundary when it is configured.
    private ApiFactory Api(string environment, StubResend? resend = null, Dictionary<string, string?>? settings = null) => new(
        database.ConnectionStringFor(DatabaseRoles.Application),
        environment: environment,
        fakeEmailChannel: false,
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

        return await api.CreateClient(admin, secondFactor: true).CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail = email });
    }

    private sealed class StubResend : HttpMessageHandler
    {
        public ConcurrentBag<(Uri Uri, string? IdempotencyKey, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"email_1"}""") };
        }
    }
}
