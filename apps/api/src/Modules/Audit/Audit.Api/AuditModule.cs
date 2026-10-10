using System.Reflection;
using Audit.Application;

namespace Audit.Api;

/// <summary>The module's API entry point. Its infrastructure is registered by <c>AuditInfrastructure</c>.</summary>
public static class AuditModule
{
    /// <summary>The assembly with the module's handlers, for the host to give Wolverine.</summary>
    public static Assembly HandlerAssembly => typeof(RecordTenantCreatedHandler).Assembly;

    /// <summary>
    /// The module's handlers of a message that a saga also handles: ControlPlane's onboarding saga handles the activation too. The
    /// host gives each a queue of its own (W5).
    /// </summary>
    public static IReadOnlyList<Type> HandlersBesideASaga => [typeof(RecordTenantCreatedHandler)];
}
