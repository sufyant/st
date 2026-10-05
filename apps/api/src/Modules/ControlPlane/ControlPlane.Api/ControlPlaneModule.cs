using System.Reflection;
using Hangfire;
using ControlPlane.Application.Roles;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api;

public static class ControlPlaneModule
{
    /// <summary>The assembly with the module's handlers and validators, for the host to give Wolverine (0023).</summary>
    public static Assembly HandlerAssembly => typeof(CreateRoleHandler).Assembly;

    public static IServiceCollection AddControlPlaneModule(this IServiceCollection services) =>
        services.AddControlPlaneInfrastructure();

    /// <summary>The module's system-defined recurring jobs (0027).</summary>
    public static void ScheduleJobs(IRecurringJobManager jobs) =>
        jobs.AddOrUpdate<CloseExpiredInvitationsJob>("controlplane.close-expired-invitations", job => job.RunAsync(CancellationToken.None), Cron.Hourly());
}
