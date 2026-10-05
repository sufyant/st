using Audit.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace Audit.Infrastructure;

internal static class AuditInfrastructure
{
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.Schema);
        services.AddScoped<IAuditLog, AuditLog>();

        return services;
    }
}
