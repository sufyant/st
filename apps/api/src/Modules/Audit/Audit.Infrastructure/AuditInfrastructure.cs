using Audit.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace Audit.Infrastructure;

/// <summary>
/// The module's infrastructure entry point: the host calls it to register the module's DbContext, adapters and contract
/// implementations.
/// </summary>
public static class AuditInfrastructure
{
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.Schema);
        services.AddScoped<IAuditLog, AuditLog>();

        return services;
    }
}
