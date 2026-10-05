namespace Notifications.Api;

// The API's own types (0010): what clients send and receive, kept apart from the commands they become.

/// <summary>A notification the signed-in user schedules for themselves; it is due at a time to come.</summary>
public sealed record ScheduledNotificationRequest(string Title, string Body, DateTimeOffset DueAt);

/// <summary>A scheduled notification and where it stands: scheduled, sent or cancelled.</summary>
public sealed record ScheduledNotificationResponse(Guid Id, string Title, string Body, DateTimeOffset DueAt, string Status);
