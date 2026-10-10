using ControlPlane.Contracts;
using Notifications.Application.Ports;
using Notifications.Contracts;

namespace Notifications.Application;

// Step 3 of the tenant onboarding: the invitation email to a new tenant's first owner. The handler takes no DbContext, so Wolverine
// opens no transaction around the call to the email service; its retries are configured from the module's settings, and once
// they are spent the event goes to the dead letter queue.
public static class SendOwnerInvitationHandler
{
    // The idempotency key is the invitation's, so an event that arrives twice sends one email.
    public static async Task<InvitationEmailSent> HandleAsync(
        OwnerInvitationReady ready,
        IEmailChannel channel,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await channel.SendAsync(
            new EmailMessage(
                ready.Email,
                $"You are invited to {ready.TenantName}",
                $"You have been invited to join {ready.TenantName}. Open this link to accept the invitation:\n\n{ready.Link}",
                $"invite/{ready.InvitationId}"),
            cancellationToken);

        var now = time.GetUtcNow();
        return new InvitationEmailSent(Guid.CreateVersion7(now), now, ready.TenantId, ready.InvitationId);
    }
}
