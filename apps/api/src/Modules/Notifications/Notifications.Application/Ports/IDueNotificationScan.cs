namespace Notifications.Application.Ports;

/// <summary>
/// Finds the scheduled notifications that are due, across all tenants, through a narrow SECURITY DEFINER lookup that reveals only
/// their tenant and id (0017). Each is then sent under its own tenant.
/// </summary>
public interface IDueNotificationScan
{
    Task<IReadOnlyList<DueNotification>> FindDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record DueNotification(Guid TenantId, Guid NotificationId);
