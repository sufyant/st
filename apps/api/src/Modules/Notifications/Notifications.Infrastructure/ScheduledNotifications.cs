using Microsoft.EntityFrameworkCore;
using Notifications.Application.Ports;
using Notifications.Domain;
using SharedKernel;
using Tenancy;

namespace Notifications.Infrastructure;

internal sealed class ScheduledNotifications(NotificationsDbContext notifications, TenantContext tenant) : IScheduledNotifications
{
    public Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("Scheduled notifications are read only in a tenant.");

    public void Add(ScheduledNotification notification) => notifications.ScheduledNotifications.Add(notification);

    public async Task<ScheduledNotification?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await notifications.ScheduledNotifications
            .FromSql($"SELECT * FROM notifications.scheduled_notifications WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return found.SingleOrDefault();
    }

    public async Task<PagedList<ScheduledNotification>> ListForRecipientAsync(
        string recipientId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var own = notifications.ScheduledNotifications.Where(notification => notification.RecipientId == recipientId);
        var total = await own.LongCountAsync(cancellationToken);
        var items = await own.AsNoTracking()
            .OrderBy(notification => notification.DueAt)
            .ThenBy(notification => notification.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new(items, page, pageSize, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => notifications.SaveChangesAsync(cancellationToken);
}
