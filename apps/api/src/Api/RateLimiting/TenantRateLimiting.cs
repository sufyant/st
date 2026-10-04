using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Tenancy;

namespace Api.RateLimiting;

// In-memory, per-pod limits from configuration (0035).
internal static class TenantRateLimiting
{
    public const string Policy = "per-tenant";

    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.Section)
            .Validate(options => options.PermitLimit > 0, "RateLimiting:PermitLimit must be positive.")
            .Validate(options => options.Window > TimeSpan.Zero, "RateLimiting:Window must be positive.")
            .ValidateOnStart();

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteProblemAsync;
            limiter.AddPolicy(Policy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = options.Window,
                    QueueLimit = 0,
                });
            });
        });
    }

    // Only a verified membership puts a request in its tenant's bucket, keyed by the resolved tenant id; the slug in the route is
    // never a key (0035).
    private static string PartitionKey(HttpContext context) =>
        context.RequestServices.GetRequiredService<TenantContext>().TenantId is { } tenant ? $"tenant:{tenant}"
        : context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? $"user:{userId}"
        : $"ip:{context.Connection.RemoteIpAddress}";

    private static async ValueTask WriteProblemAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var httpContext = rejected.HttpContext;

        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Detail = "The rate limit was exceeded. Retry after the time in the Retry-After header." },
        });
    }
}
