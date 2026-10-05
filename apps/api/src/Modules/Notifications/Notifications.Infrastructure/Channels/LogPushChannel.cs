using Microsoft.Extensions.Logging;
using Notifications.Application.Ports;

namespace Notifications.Infrastructure.Channels;

// The push channel until a client is known and its provider chosen (0037, 0045): it writes the notification to the log.
internal sealed partial class LogPushChannel(ILogger<LogPushChannel> logger) : INotificationChannel
{
    public Task SendAsync(string recipientId, Notification notification, CancellationToken cancellationToken)
    {
        LogPush(logger, recipientId, notification.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push notification {NotificationId} to {RecipientId} is not sent: no push provider yet")]
    private static partial void LogPush(ILogger logger, string recipientId, Guid notificationId);
}
