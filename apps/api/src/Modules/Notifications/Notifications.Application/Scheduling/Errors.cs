using SharedKernel;

namespace Notifications.Application.Scheduling;

internal static class Errors
{
    // Someone else's notification answers the same as one that does not exist.
    public static readonly Error NotFound = Error.NotFound("notification.not_found", "The notification was not found.");
}
