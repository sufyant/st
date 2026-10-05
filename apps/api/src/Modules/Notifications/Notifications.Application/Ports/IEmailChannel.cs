using Notifications.Contracts;

namespace Notifications.Application.Ports;

/// <summary>The email channel (0037): Resend, or the log in Development.</summary>
public interface IEmailChannel
{
    Task SendAsync(EmailMessage email, CancellationToken cancellationToken);
}
