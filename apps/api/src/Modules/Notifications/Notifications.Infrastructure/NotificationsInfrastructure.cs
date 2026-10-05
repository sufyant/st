using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Notifications.Application.Ports;
using Notifications.Contracts;
using Notifications.Infrastructure.Channels;
using Notifications.Infrastructure.Email;
using StackExchange.Redis;
using Tenancy;

namespace Notifications.Infrastructure;

internal static class NotificationsInfrastructure
{
    public const string RedisConnection = "Redis";

    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<INotificationsModule, EmailNotifications>();

        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.Schema);
        services.AddScoped<IScheduledNotifications, ScheduledNotifications>();
        services.AddScoped<IDueNotificationScan, DueNotificationScan>();

        // In-app notifications go out over SignalR. With more than one pod the Redis backplane carries them to the pod that holds
        // the user's connection; it is on when its connection string is set, and off by default (0037, 0038).
        var signalR = services.AddSignalR();
        if (configuration.GetConnectionString(RedisConnection) is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = RedisChannel.Literal(NotificationsDbContext.Schema));
        }

        services.AddSingleton<INotificationChannel, SignalRNotificationChannel>();
        services.AddSingleton<INotificationChannel, LogPushChannel>();

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
