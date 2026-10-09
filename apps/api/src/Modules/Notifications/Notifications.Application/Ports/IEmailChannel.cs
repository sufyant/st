namespace Notifications.Application.Ports;

/// <summary>The email channel: Resend, or the log in Development.</summary>
public interface IEmailChannel
{
    Task SendAsync(EmailMessage email, CancellationToken cancellationToken);
}
