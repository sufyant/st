using System.Reflection;
using ControlPlane.Application.Invitations;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api;

public static class ControlPlaneModule
{
    /// <summary>The assembly with the module's handlers and validators, for the host to give Wolverine (0023).</summary>
    public static Assembly HandlerAssembly => typeof(AcceptInvitationHandler).Assembly;

    public static IServiceCollection AddControlPlaneModule(this IServiceCollection services) =>
        services.AddControlPlaneInfrastructure();
}
