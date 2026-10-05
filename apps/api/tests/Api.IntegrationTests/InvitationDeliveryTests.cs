using System.Net;
using System.Net.Http.Json;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tenancy;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// Until the Notifications module sends invitation emails through Resend (0029, 0037), only Development writes the link to the
// log. Delivery is a message the outbox sends once the invitation is saved, so anywhere else the invitation is kept but its delivery
// fails loudly, and without a token nobody can accept it.
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
    public async Task Outside_development_the_invitation_is_kept_without_a_token_and_its_delivery_fails_explicitly()
    {
        await using var api = Api(Environments.Production);
        var email = $"{Guid.NewGuid():N}@example.com";
        HttpResponseMessage response = null!;

        var messages = await api.TrackMessagesAsync(async () => response = await InviteAsync(api, email));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        messages.MovedToErrorQueue.SingleMessage<DeliverInvitation>().ShouldNotBeNull();
        api.Services.GetFakeLogCollector().GetSnapshot()
            .ShouldContain(record => record.Exception is InvalidOperationException && record.Exception.Message.Contains("Development", StringComparison.Ordinal));
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.invitations WHERE email = '{email}' AND token_hash IS NULL")).ShouldBe(1);
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

    private ApiFactory Api(string environment) => new(
        database.ConnectionStringFor(DatabaseRoles.Application),
        environment: environment,
        fakeInvitationSender: false,
        configureServices: services => services.AddFakeLogging());

    private async Task<HttpResponseMessage> InviteAsync(ApiFactory api, string email)
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var roleId = await database.ScalarAsync<Guid>("SELECT id FROM catalog.roles WHERE built_in = 'Member'");

        return await api.CreateClient(owner).PostAsJsonAsync($"/v1/tenants/{tenant.Slug}/invitations", new { email, roleId }, Cancellation);
    }
}
