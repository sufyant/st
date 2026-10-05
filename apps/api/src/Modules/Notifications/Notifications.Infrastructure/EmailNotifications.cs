using Notifications.Application.Ports;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

// The module's synchronous contract (0009).
internal sealed class EmailNotifications(IEmailChannel channel) : INotificationsModule
{
    public Task SendEmailAsync(EmailMessage email, CancellationToken cancellationToken) => channel.SendAsync(email, cancellationToken);
}
