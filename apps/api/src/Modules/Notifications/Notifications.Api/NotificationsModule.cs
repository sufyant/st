using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Ports;
using Notifications.Infrastructure;

namespace Notifications.Api;

public static class NotificationsModule
{
    /// <summary>The assembly with the module's handlers and validators, for the host to give Wolverine.</summary>
    public static Assembly HandlerAssembly => typeof(IEmailChannel).Assembly;

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services) =>
        services.AddNotificationsInfrastructure();
}
