using ControlPlane.Application.Ports;
using Notifications.Contracts;

namespace ControlPlane.Application.Invitations;

// Sends the invitation link as email through the Notifications module (0029, 0037). The call is synchronous, so the link and its
// token never sit in a stored message. Email templates are out of scope for now (0045), so the text is plain.
internal sealed class EmailInvitationSender(INotificationsModule notifications) : IInvitationSender
{
    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken) =>
        notifications.SendEmailAsync(
            new EmailMessage(email, "You are invited", $"You have been invited to join a team. Open this link to accept the invitation:\n\n{link}"),
            cancellationToken);
}
