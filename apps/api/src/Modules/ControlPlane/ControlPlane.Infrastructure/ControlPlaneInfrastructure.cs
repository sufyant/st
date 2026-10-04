using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Infrastructure.Clerk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        // Until the Notifications module sends invitation emails through Resend (0029, 0037), only Development writes the link to
        // the log; anywhere else sending fails.
        services.AddSingleton<IInvitationSender>(provider => provider.GetRequiredService<IHostEnvironment>().IsDevelopment()
            ? ActivatorUtilities.CreateInstance<LoggingInvitationSender>(provider)
            : new UnavailableInvitationSender());

        services.AddOptions<ClerkOptions>().BindConfiguration(ClerkOptions.Section);
        services.AddHttpClient<IIdentityProvider, ClerkIdentityProvider>((provider, http) =>
        {
            var clerk = provider.GetRequiredService<IOptions<ClerkOptions>>().Value;
            http.BaseAddress = clerk.BackendApiUrl;
            http.Timeout = clerk.Timeout;
            http.DefaultRequestHeaders.Authorization = new("Bearer", clerk.SecretKey);
        });

        return services;
    }
}
