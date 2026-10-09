using Notifications.Application;
using Notifications.Application.Ports;
using ControlPlane.Contracts;
using Notifications.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Notifications.IntegrationTests;

// The Notifications module sends the invitation email when ControlPlane announces that a tenant's first owner can be invited.
public sealed class InvitationEmailTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly Guid InvitationId = new("0199a8f0-0000-7000-8000-000000000401");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri Link = new("https://clerk.test/invitations/inv_1");

    private readonly RecordingEmailChannel _channel = new();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task SendInvitation_InvitationReady_EmailsTheOwnerTheTenantsNameAndTheLink()
    {
        await SendAsync(Ready());

        var email = _channel.Sent.ShouldHaveSingleItem();
        email.To.ShouldBe("owner@acme.test");
        email.Subject.ShouldContain("Acme Ltd");
        email.Text.ShouldContain("Acme Ltd");
        email.Text.ShouldContain(Link.ToString());
    }

    // The key is the invitation's, never a new one, so an event that arrives twice does not send a second email (O4).
    [Fact]
    public async Task SendInvitation_InvitationReady_SendsUnderTheInvitationsIdempotencyKey()
    {
        await SendAsync(Ready());
        await SendAsync(Ready());

        _channel.Sent.Select(email => email.IdempotencyKey).ShouldBe([$"invite/{InvitationId}", $"invite/{InvitationId}"]);
    }

    [Fact]
    public async Task SendInvitation_EmailSent_AnnouncesItForTheInvitation()
    {
        var sent = await SendAsync(Ready());

        sent.ShouldSatisfyAllConditions(
            announced => announced.TenantId.ShouldBe(TenantId),
            announced => announced.InvitationId.ShouldBe(InvitationId),
            announced => announced.OccurredAt.ShouldBe(Now),
            announced => announced.EventId.ShouldNotBe(Guid.Empty));
    }

    [Fact]
    public async Task SendInvitation_EmailChannelFails_IsAnError()
    {
        _channel.IsDown = true;

        var send = () => SendAsync(Ready());

        await send.ShouldThrowAsync<HttpRequestException>();
    }

    private static OwnerInvitationReady Ready() =>
        new(Guid.CreateVersion7(), Now, TenantId, InvitationId, "owner@acme.test", "Acme Ltd", Link);

    private Task<InvitationEmailSent> SendAsync(OwnerInvitationReady ready) =>
        SendOwnerInvitationHandler.HandleAsync(ready, _channel, _time, Cancellation);

    // The email channel, as a system we do not own, at our port.
    private sealed class RecordingEmailChannel : IEmailChannel
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool IsDown { get; set; }

        public Task SendAsync(EmailMessage email, CancellationToken cancellationToken)
        {
            if (IsDown)
            {
                throw new HttpRequestException("The email channel is down.");
            }

            Sent.Add(email);
            return Task.CompletedTask;
        }
    }
}
