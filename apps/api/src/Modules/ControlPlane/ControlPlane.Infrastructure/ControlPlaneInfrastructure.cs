using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Contracts;
using ControlPlane.Infrastructure.Clerk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tenancy;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

/// <summary>
/// The module's infrastructure entry point: the host calls it to register the module's DbContext, adapters and contract
/// implementations.
/// </summary>
public static class ControlPlaneInfrastructure
{
    public static IServiceCollection AddControlPlaneInfrastructure(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<CatalogDbContext>(
            (provider, options) => options.UseModuleDatabase(provider, CatalogDbContext.Schema),
            TenancyServiceCollectionExtensions.MessageSchema);
        services.AddModuleMigrations<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<ISystemAdminDirectory, SystemAdminDirectory>();

        // Registered by type, so Wolverine builds them in its generated code on the handler's own DbContext, and sees that the
        // handler needs its transaction (W1).
        services.AddScoped<ITenantCatalog, TenantCatalog>();
        services.AddScoped<IUserTenants, UserTenants>();

        services.AddOptions<InvitationSettings>().BindConfiguration(InvitationSettings.Section);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<InvitationSettings>>().Value);

        services.AddTransient<IInvitationSender, EmailInvitationSender>();

        // Wolverine's generated code cannot build these two, so it resolves them from the message's scope. Neither reaches a
        // DbContext. The identity provider is a typed HTTP client, which only the container builds; the invitation sender reaches
        // the Notifications module through its contract, whose implementation that module keeps to itself.
        services.ConfigureWolverine(options =>
        {
            options.CodeGeneration.AlwaysUseServiceLocationFor<IIdentityProvider>();
            options.CodeGeneration.AlwaysUseServiceLocationFor<IInvitationSender>();
        });

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
