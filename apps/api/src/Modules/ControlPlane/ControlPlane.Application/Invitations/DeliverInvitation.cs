using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace ControlPlane.Application.Invitations;

/// <summary>
/// Sends a saved invitation to the invited person (0029). It travels through the outbox, so it runs only once the invitation is
/// committed, and it carries no token: the token is born here.
/// </summary>
public sealed record DeliverInvitation(Guid InvitationId);

public static class DeliverInvitationHandler
{
    // Clerk and the email channel are systems we do not own, and may be down for a moment: a failure is tried again after a pause
    // that doubles each time, and then the message goes to the dead letter queue. Delivery is the last step of any flow and is not
    // compensated (0026).
    public static void Configure(HandlerChain chain) =>
        chain.OnAnyException().RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

    public static async Task HandleAsync(
        DeliverInvitation message,
        ITenantCatalog catalog,
        IIdentityProvider identity,
        IInvitationSender sender,
        InvitationSettings settings,
        CancellationToken cancellationToken)
    {
        var invitation = await catalog.FindInvitationForUpdateAsync(message.InvitationId, cancellationToken)
            ?? throw new InvalidOperationException("An invitation is delivered only after it is saved.");

        // A message may arrive twice (0024): an invitation that already has its token was delivered.
        var token = InvitationToken.Generate();
        if (!invitation.IssueToken(token))
        {
            return;
        }

        var acceptLink = settings.AcceptLink(token);
        var link = await identity.HasAccountAsync(invitation.Email, cancellationToken)
            ? acceptLink
            : await identity.InviteAsync(invitation.Email, invitation.Id, acceptLink, cancellationToken);

        await catalog.SaveChangesAsync(cancellationToken);

        // Sent last: if sending fails, the token's hash rolls back with the transaction and the retry issues a new token.
        await sender.SendAsync(invitation.Email, link, cancellationToken);
    }
}
