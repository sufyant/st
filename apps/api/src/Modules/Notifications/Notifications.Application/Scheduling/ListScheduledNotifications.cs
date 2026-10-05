using FluentValidation;
using Notifications.Application.Ports;
using SharedKernel;

namespace Notifications.Application.Scheduling;

/// <summary>The user's own notifications in the active tenant, soonest first, a page at a time (0034).</summary>
public sealed record ListScheduledNotifications(string ActorId, int Page, int PageSize);

public sealed class ListScheduledNotificationsValidator : AbstractValidator<ListScheduledNotifications>
{
    public ListScheduledNotificationsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PagedList<ScheduledNotificationDetails>.MaxPageSize);
    }
}

public static class ListScheduledNotificationsHandler
{
    public static async Task<Result<PagedList<ScheduledNotificationDetails>>> HandleAsync(
        ListScheduledNotifications query,
        IScheduledNotifications notifications,
        CancellationToken cancellationToken) =>
        (await notifications.ListForRecipientAsync(query.ActorId, query.Page, query.PageSize, cancellationToken))
            .Map(ScheduledNotificationDetails.Of);
}
