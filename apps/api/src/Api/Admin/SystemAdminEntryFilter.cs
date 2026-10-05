using System.Security.Claims;
using Api.Authorization;
using Audit.Api;
using Wolverine;

namespace Api.Admin;

// Every entry of a system admin into a tenant is recorded (0031): as a security event in the log, and in the tenant's audit log
// (0040). It runs after authorization, so only entries that happen are recorded, and before the endpoint, so an entry is recorded
// whatever the command then does.
internal sealed partial class SystemAdminEntryFilter(ILoggerFactory loggers, TimeProvider time) : IEndpointFilter
{
    private readonly ILogger _security = loggers.CreateLogger("Api.Security");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Only a signed-in system admin enters a tenant.");
        LogEntry(_security, userId, http.RequestServices.GetRequiredService<RequestAccess>().AdminTenantId, http.Request.Method, http.Request.Path);

        // Tenant resolution has put the entered tenant on the request's bus, so the record is stored under it.
        await AuditTrail.RecordSystemAdminEntryAsync(
            http.RequestServices.GetRequiredService<IMessageBus>(), time, userId, $"{http.Request.Method} {http.Request.Path}");

        return await next(context);
    }

    [LoggerMessage(
        EventName = "SystemAdminTenantEntry",
        Level = LogLevel.Warning,
        Message = "System admin {UserId} entered tenant {TenantId}: {Method} {Path}")]
    private static partial void LogEntry(ILogger logger, string userId, Guid? tenantId, string method, string path);
}
