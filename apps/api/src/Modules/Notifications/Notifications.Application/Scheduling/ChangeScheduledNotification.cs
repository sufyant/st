using FluentValidation;
using Notifications.Application.Ports;
using Notifications.Domain;
using SharedKernel;

namespace Notifications.Application.Scheduling;

/// <summary>A user changes one of their own notifications before it is sent (0027).</summary>
public sealed record ChangeScheduledNotification(string ActorId, Guid NotificationId, string Title, string Body, DateTimeOffset DueAt) : IAuditedCommand
{
    object IAuditedCommand.AuditDetails => new { NotificationId, Title, DueAt };
}

public sealed class ChangeScheduledNotificationValidator : AbstractValidator<ChangeScheduledNotification>
{
    public ChangeScheduledNotificationValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(ScheduledNotification.TitleMaxLength);
        RuleFor(command => command.Body).NotNull().MaximumLength(ScheduledNotification.BodyMaxLength);
    }
}

public static class ChangeScheduledNotificationHandler
{
    public static async Task<Result<ScheduledNotificationDetails>> HandleAsync(
        ChangeScheduledNotification command,
        IScheduledNotifications notifications,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await notifications.FindForUpdateAsync(command.NotificationId, cancellationToken) is not { } notification
            || notification.RecipientId != command.ActorId)
        {
            return Errors.NotFound;
        }

        var changed = notification.Change(command.Title, command.Body, command.DueAt, time.GetUtcNow());
        if (!changed.IsSuccess)
        {
            return changed.Error;
        }

        await notifications.SaveChangesAsync(cancellationToken);
        return ScheduledNotificationDetails.Of(notification);
    }
}
