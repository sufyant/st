using ControlPlane.Application.Ports;

namespace ControlPlane.Infrastructure;

// Outside Development nothing sends invitation emails until the Notifications module does, through Resend (0029, 0037). Sending
// fails loudly, so the command's transaction rolls back and no invitation is kept whose link nobody received.
internal sealed class UnavailableInvitationSender : IInvitationSender
{
    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "Invitation emails are not sent yet: until the Notifications module sends them through Resend, only Development writes invitation links to the log.");
}
