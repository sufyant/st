namespace Notifications.Application.Ports;

/// <summary>
/// A channel that delivers a notification to a user (0037): in-app in real time, or push. The module decides what to send; the
/// channels decide how it reaches the user.
/// </summary>
public interface INotificationChannel
{
    Task SendAsync(string recipientId, Notification notification, CancellationToken cancellationToken);
}

/// <summary>What a user receives: the notification, and the tenant it belongs to.</summary>
public sealed record Notification(Guid Id, Guid TenantId, string Title, string Body);
