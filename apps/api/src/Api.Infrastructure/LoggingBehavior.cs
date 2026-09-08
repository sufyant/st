using System.Security.Claims;
using Api.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Api.Infrastructure;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger, IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var tenantId = user?.FindFirstValue("tenant_id");
        var userId = user?.FindFirstValue("sub");
        var requestId = httpContext?.TraceIdentifier;

        using (LogContext.PushProperty("tenant_id", tenantId))
        using (LogContext.PushProperty("request_id", requestId))
        using (LogContext.PushProperty("user_id", userId))
        {
            logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
            var response = await next();
            logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
            return response;
        }
    }
}
