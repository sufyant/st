using Notifications.Domain;
using SharedKernel;

namespace Notifications.Application.Ports;

/// <summary>The active tenant's scheduled notifications, under row level security (0014).</summary>
/// <remarks>
/// Public only because Wolverine's generated code passes it to public handlers (0047); its members speak domain types, so they
/// are internal to the module.
/// </remarks>
public interface IScheduledNotifications
{
    internal Guid TenantId { get; }

    internal void Add(ScheduledNotification notification);

    /// <summary>The notification, locked for the rest of the transaction, so it is changed, cancelled or sent one at a time.</summary>
    internal Task<ScheduledNotification?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    internal Task<PagedList<ScheduledNotification>> ListForRecipientAsync(string recipientId, int page, int pageSize, CancellationToken cancellationToken);

    internal Task SaveChangesAsync(CancellationToken cancellationToken);
}
