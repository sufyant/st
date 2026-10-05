using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Notifications.Application.Scheduling;
using SharedKernel;
using Wolverine;

namespace Notifications.Api;

/// <summary>
/// The module's endpoints, mapped into the host's tenant routes (0015): a user's own scheduled notifications (0027). Each endpoint
/// names the permission it needs (0030) and sends one command; the host maps its result (0032).
/// </summary>
public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this RouteGroupBuilder tenant)
    {
        var scheduled = tenant.MapGroup("/notifications/scheduled").RequireAuthorization(Permissions.NotificationsSchedule);

        scheduled.MapPost("", async (ScheduledNotificationRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            (await bus.InvokeAsync<Result<ScheduledNotificationDetails>>(
                new ScheduleNotification(user.Id(), request.Title, request.Body, request.DueAt), cancellationToken))
                .Map(ToResponse));

        scheduled.MapGet("", async (int? page, int? pageSize, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            (await bus.InvokeAsync<Result<PagedList<ScheduledNotificationDetails>>>(
                new ListScheduledNotifications(user.Id(), page ?? 1, pageSize ?? PagedList<ScheduledNotificationDetails>.DefaultPageSize), cancellationToken))
                .Map(notifications => notifications.Map(ToResponse)));

        scheduled.MapPut("/{notificationId:guid}", async (
                Guid notificationId,
                ScheduledNotificationRequest request,
                ClaimsPrincipal user,
                IMessageBus bus,
                CancellationToken cancellationToken) =>
            (await bus.InvokeAsync<Result<ScheduledNotificationDetails>>(
                new ChangeScheduledNotification(user.Id(), notificationId, request.Title, request.Body, request.DueAt), cancellationToken))
                .Map(ToResponse));

        scheduled.MapDelete("/{notificationId:guid}", (Guid notificationId, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new CancelScheduledNotification(user.Id(), notificationId), cancellationToken));
    }

    // The version group lets only signed-in users through, so the identity provider's user id is always there (0015).
    private static string Id(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request has no signed-in user.");

    private static ScheduledNotificationResponse ToResponse(ScheduledNotificationDetails notification) =>
        new(notification.Id, notification.Title, notification.Body, notification.DueAt, notification.Status);
}
