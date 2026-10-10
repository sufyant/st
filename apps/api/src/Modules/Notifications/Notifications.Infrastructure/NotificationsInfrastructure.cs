using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Application.Ports;
using Notifications.Infrastructure.Email;
using Wolverine;

namespace Notifications.Infrastructure;

/// <summary>
/// The module's infrastructure entry point: the host calls it to register the module's adapters. The module stores no data, so it
/// has no DbContext and no migrations.
/// </summary>
public static class NotificationsInfrastructure
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services)
    {
        // Outside Development email goes only through Resend. Without its settings no email could leave, invitations included, so the
        // application does not start.
        services.AddOptions<ResendOptions>()
            .BindConfiguration(ResendOptions.Section)
            .Validate<IHostEnvironment>(
                (resend, environment) => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(resend.ApiKey),
                $"{ResendOptions.Section}:ApiKey must be set outside Development, or no email is sent.")
            .Validate<IHostEnvironment>(
                (resend, environment) => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(resend.From),
                $"{ResendOptions.Section}:From must be set outside Development, or no email is sent.")
            .Validate(resend => resend.ApiUrl.IsAbsoluteUri, $"{ResendOptions.Section}:ApiUrl must be an absolute URL.")
            .Validate(resend => resend.Timeout > TimeSpan.Zero, $"{ResendOptions.Section}:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<ResendEmailChannel>((provider, http) =>
        {
            var resend = provider.GetRequiredService<IOptions<ResendOptions>>().Value;
            http.BaseAddress = resend.ApiUrl;
            http.DefaultRequestHeaders.Authorization = new("Bearer", resend.ApiKey);
            http.Timeout = resend.Timeout;
        });

        // Development without Resend writes email to the log; anywhere else email goes through Resend.
        services.AddTransient<IEmailChannel>(provider =>
            provider.GetRequiredService<IOptions<ResendOptions>>().Value.IsConfigured
            || !provider.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? provider.GetRequiredService<ResendEmailChannel>()
                : ActivatorUtilities.CreateInstance<LogEmailChannel>(provider));

        // Wolverine's generated code cannot build the email channel, which the container chooses by environment, so it resolves it
        // from the message's scope. It reaches no DbContext.
        services.ConfigureWolverine(options => options.CodeGeneration.AlwaysUseServiceLocationFor<IEmailChannel>());

        services.AddOptions<NotificationsSettings>()
            .BindConfiguration(NotificationsSettings.Section)
            .Validate(settings => settings.IsValid, $"{NotificationsSettings.Section}:InvitationEmailRetryDelays must be positive pauses that grow.")
            .ValidateOnStart();
        services.AddSingleton<IWolverineExtension, InvitationEmailRetries>();

        return services;
    }
}
