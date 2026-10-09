using System.Reflection;
using ControlPlane.Application.Invitations;

namespace ControlPlane.Api;

/// <summary>
/// The module's API entry point, with <see cref="ControlPlaneEndpoints"/>. Its infrastructure is registered by
/// <c>ControlPlaneInfrastructure</c>.
/// </summary>
public static class ControlPlaneModule
{
    /// <summary>The assembly with the module's handlers and validators, for the host to give Wolverine.</summary>
    public static Assembly HandlerAssembly => typeof(AcceptInvitationHandler).Assembly;
}
