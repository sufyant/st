using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Infrastructure.Clerk;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<IUserTenants, UserTenants>();

        services.AddOptions<InvitationSettings>().BindConfiguration(InvitationSettings.Section);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<InvitationSettings>>().Value);

        services.AddTransient<IInvitationSender, EmailInvitationSender>();

        services.AddOptions<ClerkOptions>().BindConfiguration(ClerkOptions.Section);
        services.AddHttpClient<IIdentityProvider, ClerkIdentityProvider>((provider, http) =>
        {
            var clerk = provider.GetRequiredService<IOptions<ClerkOptions>>().Value;
            http.BaseAddress = clerk.BackendApiUrl;
            http.DefaultRequestHeaders.Authorization = new("Bearer", clerk.SecretKey);
            http.Timeout = clerk.Timeout;
        });

        return services;
    }
}
