using Notifications.Application.Ports;
using SharedKernel;

namespace Notifications.Application.Scheduling;

/// <summary>A user cancels one of their own notifications before it is sent (0027).</summary>
public sealed record CancelScheduledNotification(string ActorId, Guid NotificationId) : IAuditedCommand
{
    object IAuditedCommand.AuditDetails => new { NotificationId };
}

public static class CancelScheduledNotificationHandler
{
    public static async Task<Result> HandleAsync(
        CancelScheduledNotification command,
        IScheduledNotifications notifications,
        CancellationToken cancellationToken)
    {
        if (await notifications.FindForUpdateAsync(command.NotificationId, cancellationToken) is not { } notification
            || notification.RecipientId != command.ActorId)
        {
            return Errors.NotFound;
        }

        var cancelled = notification.Cancel();
        if (!cancelled.IsSuccess)
        {
            return cancelled;
        }

        await notifications.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
