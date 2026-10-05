using FluentValidation;
using Notifications.Application.Ports;
using Notifications.Domain;
using SharedKernel;

namespace Notifications.Application.Scheduling;

/// <summary>A user schedules a notification for themselves in the active tenant, such as "remind me in three days" (0027).</summary>
public sealed record ScheduleNotification(string ActorId, string Title, string Body, DateTimeOffset DueAt) : IAuditedCommand
{
    object IAuditedCommand.AuditDetails => new { Title, DueAt };
}

public sealed class ScheduleNotificationValidator : AbstractValidator<ScheduleNotification>
{
    public ScheduleNotificationValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(ScheduledNotification.TitleMaxLength);
        RuleFor(command => command.Body).NotNull().MaximumLength(ScheduledNotification.BodyMaxLength);
    }
}

public static class ScheduleNotificationHandler
{
    public static async Task<Result<ScheduledNotificationDetails>> HandleAsync(
        ScheduleNotification command,
        IScheduledNotifications notifications,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var scheduled = ScheduledNotification.Schedule(Guid.CreateVersion7(now), command.ActorId, command.Title, command.Body, command.DueAt, now);
        if (!scheduled.IsSuccess)
        {
            return scheduled.Error;
        }

        notifications.Add(scheduled.Value);
        await notifications.SaveChangesAsync(cancellationToken);
        return ScheduledNotificationDetails.Of(scheduled.Value);
    }
}
