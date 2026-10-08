using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Notifications.Application.Ports;
using Notifications.Contracts;
using Notifications.Infrastructure.Email;
using Tenancy;

namespace Notifications.Infrastructure;

internal static class NotificationsInfrastructure
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services)
    {
        services.AddTransient<INotificationsModule, EmailNotifications>();

        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.Schema);

        services.AddOptions<ResendOptions>().BindConfiguration(ResendOptions.Section);
        services.AddHealthChecks().AddCheck<EmailChannelHealthCheck>("email", tags: ["ready"]);

        // Timeouts, retries and a circuit breaker (0041). A retried send keeps its idempotency key, so it is safe to repeat.
        var resendResilience = services.AddHttpClient<ResendEmailChannel>((provider, http) =>
            {
                var resend = provider.GetRequiredService<IOptions<ResendOptions>>().Value;
                http.BaseAddress = resend.ApiUrl;
                http.DefaultRequestHeaders.Authorization = new("Bearer", resend.ApiKey);
            })
            .AddStandardResilienceHandler();
        services.AddOptions<HttpStandardResilienceOptions>(resendResilience.PipelineName)
            .Configure<IOptions<ResendOptions>>((resilience, resend) =>
            {
                resilience.AttemptTimeout.Timeout = resend.Value.Timeout;
                resilience.Retry.Delay = resend.Value.RetryDelay;
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
