using SharedKernel;

namespace Notifications.Domain;

/// <summary>
/// A notification a user schedules for themselves, such as "remind me in three days" (0027). It belongs to its tenant, can be
/// changed or cancelled until it is sent, and is sent once, when it is due.
/// </summary>
internal sealed class ScheduledNotification : ITenantEntity
{
    public const int RecipientIdMaxLength = 255;

    public const int TitleMaxLength = 200;

    public const int BodyMaxLength = 2000;

    private static readonly Error DueInPast = Error.Validation("notification.due_in_past", "A notification is scheduled for a time to come.");

    private static readonly Error NotScheduled = Error.Conflict("notification.not_scheduled", "The notification was already sent or cancelled.");

    private ScheduledNotification(Guid id, string recipientId, string title, string body, DateTimeOffset dueAt, DateTimeOffset createdAt)
    {
        Id = id;
        RecipientId = recipientId;
        Title = title;
        Body = body;
        DueAt = dueAt;
        CreatedAt = createdAt;
        Status = NotificationStatus.Scheduled;
    }

    public Guid Id { get; private init; }

    /// <summary>The identity provider's id of the user who scheduled it and receives it.</summary>
    public string RecipientId { get; private init; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static Result<ScheduledNotification> Schedule(
        Guid id,
        string recipientId,
        string title,
        string body,
        DateTimeOffset dueAt,
        DateTimeOffset now) =>
        dueAt <= now ? DueInPast : new ScheduledNotification(id, recipientId, title, body, dueAt, now);

    public Result Change(string title, string body, DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (Status != NotificationStatus.Scheduled)
        {
            return NotScheduled;
        }

        if (dueAt <= now)
        {
            return DueInPast;
        }

        (Title, Body, DueAt) = (title, body, dueAt);
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status != NotificationStatus.Scheduled)
        {
            return NotScheduled;
        }

        Status = NotificationStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>Marks a due notification sent; false when it is not due, or was already sent or cancelled.</summary>
    public bool MarkSent(DateTimeOffset now)
    {
        if (Status != NotificationStatus.Scheduled || now < DueAt)
        {
            return false;
        }

        Status = NotificationStatus.Sent;
        SentAt = now;
        return true;
    }
}

internal enum NotificationStatus
{
    Scheduled,
    Sent,
    Cancelled,
}
