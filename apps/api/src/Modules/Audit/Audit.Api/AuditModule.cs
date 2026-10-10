using System.Reflection;
using Audit.Application;

namespace Audit.Api;

/// <summary>The module's API entry point. Its infrastructure is registered by <c>AuditInfrastructure</c>.</summary>
public static class AuditModule
{
    /// <summary>The assembly with the module's handlers, for the host to give Wolverine.</summary>
    public static Assembly HandlerAssembly => typeof(RecordTenantCreatedHandler).Assembly;
}
