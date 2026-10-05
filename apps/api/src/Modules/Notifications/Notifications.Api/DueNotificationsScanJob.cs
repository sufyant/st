using Hangfire;
using Notifications.Application.Delivery;
using Wolverine;

namespace Notifications.Api;

/// <summary>
/// Hangfire's scanner of user-defined schedules, every minute (0027). Its work is a command, so it passes the host's pipeline like
/// any other; a scan still running holds the next one back.
/// </summary>
public sealed class DueNotificationsScanJob(IMessageBus bus)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task RunAsync(CancellationToken cancellationToken) => bus.InvokeAsync(new ScanDueNotifications(), cancellationToken);
}
