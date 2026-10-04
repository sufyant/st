using System.Security.Claims;
using Api.Authorization;

namespace Api.Admin;

// Every entry of a system admin into a tenant is recorded (0031). It runs after authorization, so only entries that happen are
// recorded. Until the audit log exists (0040) the record is a security event in the log; the audit record joins it here.
internal sealed partial class SystemAdminEntryFilter(ILoggerFactory loggers) : IEndpointFilter
{
    private readonly ILogger _security = loggers.CreateLogger("Api.Security");

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        LogEntry(
            _security,
            http.User.FindFirstValue(ClaimTypes.NameIdentifier),
            http.RequestServices.GetRequiredService<RequestAccess>().AdminTenantId,
            http.Request.Method,
            http.Request.Path);

        return next(context);
    }

    [LoggerMessage(
        EventName = "SystemAdminTenantEntry",
        Level = LogLevel.Warning,
        Message = "System admin {UserId} entered tenant {TenantId}: {Method} {Path}")]
    private static partial void LogEntry(ILogger logger, string? userId, Guid? tenantId, string method, string path);
}
