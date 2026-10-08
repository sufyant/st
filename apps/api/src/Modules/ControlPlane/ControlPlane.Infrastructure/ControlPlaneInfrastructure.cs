using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Infrastructure.Clerk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Tenancy;

namespace ControlPlane.Infrastructure;

internal static class ControlPlaneInfrastructure
{
    public static IServiceCollection AddControlPlaneInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<ISystemAdminDirectory, SystemAdminDirectory>();
        services.AddScoped<TenantCatalog>();
        services.AddScoped<ITenantCatalog>(provider => provider.GetRequiredService<TenantCatalog>());
        services.AddScoped<IInvitationDirectory, InvitationDirectory>();
        services.AddSingleton<ITenantReport, TenantReport>();

        services.AddOptions<InvitationSettings>().BindConfiguration(InvitationSettings.Section);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<InvitationSettings>>().Value);

        services.AddTransient<IInvitationSender, EmailInvitationSender>();

        services.AddOptions<ClerkOptions>().BindConfiguration(ClerkOptions.Section);
        // Timeouts, retries and a circuit breaker (0041); the resilience handler bounds each attempt and the whole call. Creating a
        // Clerk invitation is safe to repeat, because it ignores an earlier pending one (0029).
        var clerkResilience = services.AddHttpClient<IIdentityProvider, ClerkIdentityProvider>((provider, http) =>
            {
                var clerk = provider.GetRequiredService<IOptions<ClerkOptions>>().Value;
                http.BaseAddress = clerk.BackendApiUrl;
                http.DefaultRequestHeaders.Authorization = new("Bearer", clerk.SecretKey);
            })
            .AddStandardResilienceHandler();
        services.AddOptions<HttpStandardResilienceOptions>(clerkResilience.PipelineName)
            .Configure<IOptions<ClerkOptions>>((resilience, clerk) =>
            {
                resilience.AttemptTimeout.Timeout = clerk.Value.Timeout;
                resilience.Retry.Delay = clerk.Value.RetryDelay;
            });

        return services;
    }
}
