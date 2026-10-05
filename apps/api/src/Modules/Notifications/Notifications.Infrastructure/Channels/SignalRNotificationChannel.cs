using Microsoft.AspNetCore.SignalR;
using Notifications.Application.Ports;

namespace Notifications.Infrastructure.Channels;

// In-app notifications in real time (0037). With the Redis backplane on, a notification reaches the user's connections on every
// pod, not only on the pod that sends it.
internal sealed class SignalRNotificationChannel(IHubContext<NotificationsHub> hub) : INotificationChannel
{
    public Task SendAsync(string recipientId, Notification notification, CancellationToken cancellationToken) =>
        hub.Clients.User(recipientId).SendAsync(NotificationsHub.Method, notification, cancellationToken);
}
