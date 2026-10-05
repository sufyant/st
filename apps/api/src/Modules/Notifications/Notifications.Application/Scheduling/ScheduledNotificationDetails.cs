using Notifications.Domain;

namespace Notifications.Application.Scheduling;

public sealed record ScheduledNotificationDetails(Guid Id, string Title, string Body, DateTimeOffset DueAt, string Status)
{
    internal static ScheduledNotificationDetails Of(ScheduledNotification notification) =>
        new(notification.Id, notification.Title, notification.Body, notification.DueAt, notification.Status.ToString());
}
