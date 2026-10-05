using Notifications.Domain;

namespace Notifications.UnitTests;

// A user-defined notification: scheduled for a time to come, editable and cancellable until it is sent, and sent once (0027).
public sealed class ScheduledNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_notification_is_scheduled_for_a_time_to_come()
    {
        var scheduled = Schedule(dueAt: Now.AddDays(3));

        scheduled.IsSuccess.ShouldBeTrue();
        (scheduled.Value.Status, scheduled.Value.DueAt, scheduled.Value.RecipientId).ShouldBe((NotificationStatus.Scheduled, Now.AddDays(3), "user_ada"));
    }

    [Fact]
    public void A_notification_cannot_be_scheduled_in_the_past()
    {
        var scheduled = Schedule(dueAt: Now.AddMinutes(-1));

        scheduled.Error.Code.ShouldBe("notification.due_in_past");
    }

    [Fact]
    public void A_scheduled_notification_can_be_changed()
    {
        var notification = Schedule(dueAt: Now.AddDays(3)).Value;

        var changed = notification.Change("Call Grace", "About the launch", Now.AddDays(5), Now);

        changed.IsSuccess.ShouldBeTrue();
        (notification.Title, notification.Body, notification.DueAt).ShouldBe(("Call Grace", "About the launch", Now.AddDays(5)));
    }

    [Fact]
    public void A_change_cannot_move_a_notification_into_the_past()
    {
        var notification = Schedule(dueAt: Now.AddDays(3)).Value;

        var changed = notification.Change("Call Grace", "About the launch", Now.AddMinutes(-1), Now);

        changed.Error.Code.ShouldBe("notification.due_in_past");
        notification.DueAt.ShouldBe(Now.AddDays(3));
    }

    [Fact]
    public void A_cancelled_notification_can_be_neither_changed_nor_sent()
    {
        var notification = Schedule(dueAt: Now.AddDays(3)).Value;
        notification.Cancel().IsSuccess.ShouldBeTrue();

        var changed = notification.Change("Call Grace", "About the launch", Now.AddDays(5), Now);
        var sent = notification.MarkSent(Now.AddDays(4));

        changed.Error.Code.ShouldBe("notification.not_scheduled");
        sent.ShouldBeFalse();
        notification.Status.ShouldBe(NotificationStatus.Cancelled);
    }

    [Fact]
    public void A_notification_is_sent_once_it_is_due()
    {
        var notification = Schedule(dueAt: Now.AddDays(3)).Value;

        var early = notification.MarkSent(Now.AddDays(2));
        var due = notification.MarkSent(Now.AddDays(3));
        var again = notification.MarkSent(Now.AddDays(3));

        (early, due, again).ShouldBe((false, true, false));
        (notification.Status, notification.SentAt).ShouldBe((NotificationStatus.Sent, Now.AddDays(3)));
    }

    [Fact]
    public void A_sent_notification_cannot_be_cancelled()
    {
        var notification = Schedule(dueAt: Now.AddDays(3)).Value;
        notification.MarkSent(Now.AddDays(3));

        var cancelled = notification.Cancel();

        cancelled.Error.Code.ShouldBe("notification.not_scheduled");
    }

    private static SharedKernel.Result<ScheduledNotification> Schedule(DateTimeOffset dueAt) =>
        ScheduledNotification.Schedule(Guid.CreateVersion7(Now), "user_ada", "Call Ada", "About the launch", dueAt, Now);
}
