using System.Security.Claims;
using Application.Abstractions;
using Serilog.Context;

namespace Api.Http;

public sealed class RequestEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        // Pushed here rather than in the pipeline behavior so that logs written outside a request
        // handler, by middleware or the outbox worker, carry the same fields.
        using (LogContext.PushProperty("request_id", context.TraceIdentifier))
        using (LogContext.PushProperty("user_id", context.User.FindFirstValue("sub")))
        using (LogContext.PushProperty(
                   "tenant_id",
                   tenantContext.IsResolved ? tenantContext.TenantId : null))
        {
            await next(context);
        }
    }
}
