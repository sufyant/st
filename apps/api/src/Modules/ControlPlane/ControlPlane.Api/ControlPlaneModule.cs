using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api;

public static class ControlPlaneModule
{
    public static IServiceCollection AddControlPlaneModule(this IServiceCollection services) =>
        services.AddControlPlaneInfrastructure();
}
