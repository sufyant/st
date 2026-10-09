using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notifications.Application.Ports;
using Notifications.Contracts;
using Notifications.Infrastructure.Email;
using Tenancy;
using Wolverine.EntityFrameworkCore;

namespace Notifications.Infrastructure;

/// <summary>
/// The module's infrastructure entry point: the host calls it to register the module's DbContext, adapters and contract
/// implementations.
/// </summary>
public static class NotificationsInfrastructure
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services)
    {
        services.AddTransient<INotificationsModule, EmailNotifications>();

        services.AddDbContextWithWolverineIntegration<NotificationsDbContext>(
            (provider, options) => options.UseModuleDatabase(provider, NotificationsDbContext.Schema),
            TenancyServiceCollectionExtensions.MessageSchema);
        services.AddModuleMigrations<NotificationsDbContext>(NotificationsDbContext.Schema);

        services.AddOptions<ResendOptions>().BindConfiguration(ResendOptions.Section);
        services.AddHealthChecks().AddCheck<EmailChannelHealthCheck>("email", tags: ["ready"]);

        services.AddHttpClient<ResendEmailChannel>((provider, http) =>
        {
            var resend = provider.GetRequiredService<IOptions<ResendOptions>>().Value;
            http.BaseAddress = resend.ApiUrl;
            http.DefaultRequestHeaders.Authorization = new("Bearer", resend.ApiKey);
            http.Timeout = resend.Timeout;
        });

        // Development without Resend writes email to the log; anywhere else email goes through Resend, and the readiness check
        // keeps the pod out of traffic until Resend is configured.
        services.AddTransient<IEmailChannel>(provider =>
            provider.GetRequiredService<IOptions<ResendOptions>>().Value.IsConfigured
            || !provider.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? provider.GetRequiredService<ResendEmailChannel>()
                : ActivatorUtilities.CreateInstance<LogEmailChannel>(provider));

        return services;
    }
}
