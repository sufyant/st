using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
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
        services.AddOptions<SystemAdminSettings>().BindConfiguration(SystemAdminSettings.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SystemAdminSettings>, SystemAdminSettings.Validation>();

        // Registered by type, so Wolverine builds them in its generated code on the handler's own DbContext, and sees that the
        // handler needs its transaction (W1).
        services.AddScoped<ITenantCatalog, TenantCatalog>();
        services.AddScoped<IUserTenants, UserTenants>();

        services.AddOptions<InvitationSettings>()
            .BindConfiguration(InvitationSettings.Section)
            .Validate(settings => settings.AcceptUrl is { IsAbsoluteUri: true }, $"{InvitationSettings.Section}:AcceptUrl must be the absolute URL of the page that accepts invitations.")
            .Validate(settings => settings.Lifetime > TimeSpan.Zero, $"{InvitationSettings.Section}:Lifetime must be positive.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<InvitationSettings>>().Value);

        services.AddOptions<OnboardingSettings>()
            .BindConfiguration(OnboardingSettings.Section)
            .Validate(settings => !settings.Problems().Any(), $"{OnboardingSettings.Section}:RegistrationTimeout, InvitationEmailTimeout and IdentityProviderRetryDelays must be positive, and the timeouts longer than the retry delays together.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<OnboardingSettings>>().Value);
        services.AddSingleton<IWolverineExtension, IdentityProviderRetries>();
        services.AddSingleton<OnboardingAlarm>();

        // Wolverine's generated code cannot build the identity provider, a typed HTTP client that only the container builds, so it
        // resolves it from the message's scope. It reaches no DbContext.
        services.ConfigureWolverine(options => options.CodeGeneration.AlwaysUseServiceLocationFor<IIdentityProvider>());

        services.AddOptions<ClerkOptions>()
            .BindConfiguration(ClerkOptions.Section)
            .Validate(clerk => !string.IsNullOrWhiteSpace(clerk.SecretKey), $"{ClerkOptions.Section}:SecretKey must be the secret key of the Clerk instance.")
            .Validate(clerk => clerk.BackendApiUrl.IsAbsoluteUri, $"{ClerkOptions.Section}:BackendApiUrl must be an absolute URL.")
            .Validate(clerk => clerk.Timeout > TimeSpan.Zero, $"{ClerkOptions.Section}:Timeout must be positive.")
            .ValidateOnStart();
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
