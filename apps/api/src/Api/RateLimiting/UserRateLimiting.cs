using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.RateLimiting;

// In-memory, per-pod limits from configuration. API4: the bucket is the signed-in user, so one member of a tenant cannot use up the
// requests of the others; a caller without a user is limited by their address.
internal static class UserRateLimiting
{
    public const string Policy = "per-user";

    public static IServiceCollection AddUserRateLimiting(this IServiceCollection services)
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

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? $"user:{userId}" : $"ip:{context.Connection.RemoteIpAddress}";

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
