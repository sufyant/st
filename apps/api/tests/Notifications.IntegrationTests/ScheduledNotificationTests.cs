using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Delivery;
using Notifications.Application.Ports;
using Notifications.Application.Scheduling;
using SharedKernel;

namespace Notifications.IntegrationTests;

// User-defined scheduled notifications (0027): a user's own, in their tenant, sent once when due through every channel (0037).
public sealed class ScheduledNotificationTests(Database database) : IAsyncDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChannel _channel = new();
    private ServiceProvider? _services;

    private ServiceProvider Services => _services ??= database.BuildServices(_channel, _time);

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_user_sees_only_their_own_notifications()
    {
        var tenant = Guid.NewGuid();
        await ScheduleAsync(tenant, "user_ada", "Call Grace");
        await ScheduleAsync(tenant, "user_grace", "Call Ada");

        var listed = await InTenant.RunAsync(Services, tenant, scope => ListScheduledNotificationsHandler.HandleAsync(
            new ListScheduledNotifications("user_ada", 1, 10), scope.GetRequiredService<IScheduledNotifications>(), Cancellation));

        listed.Value.Items.Select(notification => notification.Title).ShouldBe(["Call Grace"]);
    }

    // Someone else's notification answers as one that does not exist.
    [Fact]
    public async Task A_user_cannot_change_or_cancel_someone_elses_notification()
    {
        var tenant = Guid.NewGuid();
        var graces = await ScheduleAsync(tenant, "user_grace", "Call Ada");

        var changed = await InTenant.RunAsync(Services, tenant, scope => ChangeScheduledNotificationHandler.HandleAsync(
            new ChangeScheduledNotification("user_ada", graces.Id, "Taken over", "", _time.GetUtcNow().AddDays(5)),
            scope.GetRequiredService<IScheduledNotifications>(),
            _time,
            Cancellation));
        var cancelled = await InTenant.RunAsync(Services, tenant, scope => CancelScheduledNotificationHandler.HandleAsync(
            new CancelScheduledNotification("user_ada", graces.Id), scope.GetRequiredService<IScheduledNotifications>(), Cancellation));

        changed.Error.Code.ShouldBe("notification.not_found");
        cancelled.Error.Code.ShouldBe("notification.not_found");
    }

    // The scanner learns which notifications are due, in every tenant, and nothing else about them (0017).
    [Fact]
    public async Task The_scan_finds_the_due_notifications_of_every_tenant()
    {
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var due = await ScheduleAsync(first, "user_ada", "Due", inDays: 1);
        var alsoDue = await ScheduleAsync(second, "user_grace", "Also due", inDays: 2);
        var later = await ScheduleAsync(first, "user_ada", "Later", inDays: 5);
        var cancelled = await ScheduleAsync(second, "user_grace", "Cancelled", inDays: 1);
        await InTenant.RunAsync(Services, second, scope => CancelScheduledNotificationHandler.HandleAsync(
            new CancelScheduledNotification("user_grace", cancelled.Id), scope.GetRequiredService<IScheduledNotifications>(), Cancellation));
        _time.Advance(TimeSpan.FromDays(3));

        var found = await FindDueAsync();

        found.ShouldContain(new DueNotification(first, due.Id));
        found.ShouldContain(new DueNotification(second, alsoDue.Id));
        found.ShouldNotContain(item => item.NotificationId == later.Id || item.NotificationId == cancelled.Id);
    }

    // The interaction is the requirement: however often a due notification is dispatched, it reaches its user once (0042).
    [Fact]
    public async Task A_due_notification_is_sent_once_through_every_channel()
    {
        var tenant = Guid.NewGuid();
        var notification = await ScheduleAsync(tenant, "user_ada", "Call Grace", inDays: 1);
        _time.Advance(TimeSpan.FromDays(1));

        await DispatchAsync(tenant, notification.Id);
        await DispatchAsync(tenant, notification.Id);

        var sent = _channel.Sent.ShouldHaveSingleItem();
        sent.RecipientId.ShouldBe("user_ada");
        sent.Notification.ShouldBe(new Notification(notification.Id, tenant, "Call Grace", "Body of Call Grace"));
        (await FindDueAsync()).ShouldNotContain(item => item.NotificationId == notification.Id);
    }

    [Fact]
    public async Task A_notification_that_is_not_due_yet_is_not_sent()
    {
        var tenant = Guid.NewGuid();
        var notification = await ScheduleAsync(tenant, "user_ada", "Call Grace", inDays: 1);

        await DispatchAsync(tenant, notification.Id);

        _channel.Sent.ShouldBeEmpty();
    }

    private async Task<ScheduledNotificationDetails> ScheduleAsync(Guid tenant, string userId, string title, int inDays = 3)
    {
        var scheduled = await InTenant.RunAsync(Services, tenant, scope => ScheduleNotificationHandler.HandleAsync(
            new ScheduleNotification(userId, title, $"Body of {title}", _time.GetUtcNow().AddDays(inDays)),
            scope.GetRequiredService<IScheduledNotifications>(),
            _time,
            Cancellation));

        return scheduled.Value;
    }

    private async Task<IReadOnlyList<DueNotification>> FindDueAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDueNotificationScan>().FindDueAsync(_time.GetUtcNow(), Cancellation);
    }

    private Task DispatchAsync(Guid tenant, Guid notificationId) =>
        InTenant.ProcessAsync(Services, tenant, scope => DispatchScheduledNotificationHandler.HandleAsync(
            new DispatchScheduledNotification(notificationId),
            scope.GetRequiredService<IScheduledNotifications>(),
            scope.GetServices<INotificationChannel>(),
            _time,
            Cancellation));
}
