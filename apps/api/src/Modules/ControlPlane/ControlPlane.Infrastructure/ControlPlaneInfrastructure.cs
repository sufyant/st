using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace ControlPlane.Infrastructure;

internal static class ControlPlaneInfrastructure
{
    public static IServiceCollection AddControlPlaneInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<TenantCatalog>();

        return services;
    }
}
