using Notifications.Application.Ports;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Notifications.Application.Delivery;

/// <summary>Delivers one due notification to its recipient through every channel (0037), in the notification's tenant.</summary>
public sealed record DispatchScheduledNotification(Guid NotificationId);

public static class DispatchScheduledNotificationHandler
{
    // The channels may be down for a moment: a failure is tried again after a pause that doubles each time, and then the message
    // goes to the dead letter queue.
    public static void Configure(HandlerChain chain) =>
        chain.OnAnyException().RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

    public static async Task HandleAsync(
        DispatchScheduledNotification message,
        IScheduledNotifications notifications,
        IEnumerable<INotificationChannel> channels,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // The scanner may find a notification again before it is sent, and a message may arrive twice (0024): only the first
        // dispatch of a due notification sends it. The row stays locked until the transaction ends.
        if (await notifications.FindForUpdateAsync(message.NotificationId, cancellationToken) is not { } notification
            || !notification.MarkSent(time.GetUtcNow()))
        {
            return;
        }

        await notifications.SaveChangesAsync(cancellationToken);

        // Sent last: if a channel fails, the transaction rolls back and the retry sends it again.
        var sent = new Notification(notification.Id, notifications.TenantId, notification.Title, notification.Body);
        foreach (var channel in channels)
        {
            await channel.SendAsync(notification.RecipientId, sent, cancellationToken);
        }
    }
}
