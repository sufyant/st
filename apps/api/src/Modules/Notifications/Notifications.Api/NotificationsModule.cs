using System.Reflection;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Ports;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Channels;

namespace Notifications.Api;

public static class NotificationsModule
{
    /// <summary>The assembly with the module's handlers and validators, for the host to give Wolverine (0023).</summary>
    public static Assembly HandlerAssembly => typeof(IEmailChannel).Assembly;

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddNotificationsInfrastructure(configuration);

    /// <summary>The module's system-defined recurring jobs (0027).</summary>
    public static void ScheduleJobs(IRecurringJobManager jobs) =>
        jobs.AddOrUpdate<DueNotificationsScanJob>("notifications.scan-due-notifications", job => job.RunAsync(CancellationToken.None), Cron.Minutely());

    /// <summary>The real-time in-app channel (0037), for signed-in users, under the version group.</summary>
    public static void MapNotificationsHub(this RouteGroupBuilder v1) => v1.MapHub<NotificationsHub>("/notifications/hub");
}
