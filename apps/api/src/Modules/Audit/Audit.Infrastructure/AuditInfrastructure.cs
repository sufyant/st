using Audit.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;
using Wolverine.EntityFrameworkCore;

namespace Audit.Infrastructure;

/// <summary>
/// The module's infrastructure entry point: the host calls it to register the module's DbContext, adapters and contract
/// implementations.
/// </summary>
public static class AuditInfrastructure
{
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<AuditDbContext>(
            (provider, options) => options.UseModuleDatabase(provider, AuditDbContext.Schema),
            TenancyServiceCollectionExtensions.MessageSchema);
        services.AddModuleMigrations<AuditDbContext>(AuditDbContext.Schema);

        // Registered by type, so Wolverine builds it in its generated code on the handler's own DbContext (W1).
        services.AddScoped<IAuditLog, AuditLog>();

        return services;
    }
}
