using System.Reflection;
using Audit.Application;
using Audit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Audit.Api;

public static class AuditModule
{
    /// <summary>The assembly with the module's handlers, for the host to give Wolverine.</summary>
    public static Assembly HandlerAssembly => typeof(RecordAuditEntryHandler).Assembly;

    public static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services.AddAuditInfrastructure();
}
