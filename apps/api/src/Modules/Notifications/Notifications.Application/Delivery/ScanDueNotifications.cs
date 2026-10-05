using Notifications.Application.Ports;
using Wolverine;

namespace Notifications.Application.Delivery;

/// <summary>
/// Finds the scheduled notifications that are due in every tenant and sends each one to be delivered under its own tenant (0017,
/// 0027). The scanner job sends it every minute; it runs outside any tenant and writes nothing.
/// </summary>
public sealed record ScanDueNotifications;

public static class ScanDueNotificationsHandler
{
    public static async Task HandleAsync(
        ScanDueNotifications command,
        IDueNotificationScan scan,
        IMessageContext context,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        foreach (var due in await scan.FindDueAsync(time.GetUtcNow(), cancellationToken))
        {
            await context.PublishAsync(new DispatchScheduledNotification(due.NotificationId), new DeliveryOptions { TenantId = due.TenantId.ToString() });
        }
    }
}
