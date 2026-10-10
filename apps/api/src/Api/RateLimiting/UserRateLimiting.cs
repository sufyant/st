using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ControlPlane.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.RateLimiting;

// In-memory, per-pod limits from configuration. API4: the bucket is the signed-in user, so one member of a tenant cannot use up the
// requests of the others; a caller without a user is limited by their address. ASP.NET Core applies one policy to an endpoint, so
// the general limit is the global limiter, for every endpoint that asks for it, and an endpoint's own stricter limit is a policy that
// applies on top of it.
internal static class UserRateLimiting
{
    private const string Unlimited = "unlimited";

    public static IServiceCollection AddUserRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.Section)
            .Validate(options => options.PermitLimit > 0, "RateLimiting:PermitLimit must be positive.")
            .Validate(options => options.Window > TimeSpan.Zero, "RateLimiting:Window must be positive.")
            .Validate(options => options.InvitationAccept.PermitLimit > 0, "RateLimiting:InvitationAccept:PermitLimit must be positive.")
            .Validate(options => options.InvitationAccept.Window > TimeSpan.Zero, "RateLimiting:InvitationAccept:Window must be positive.")
            .ValidateOnStart();

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteProblemAsync;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<UserRateLimit>() is null
                    ? RateLimitPartition.GetNoLimiter(Unlimited)
                    : PerUser(context, Options(context).PermitLimit, Options(context).Window));
            limiter.AddPolicy(ControlPlaneEndpoints.InvitationAcceptRateLimit, context =>
                PerUser(context, Options(context).InvitationAccept.PermitLimit, Options(context).InvitationAccept.Window));
        });
    }

    /// <summary>Puts the endpoints under the general limit for each user.</summary>
    public static TBuilder RequireUserRateLimit<TBuilder>(this TBuilder endpoints)
        where TBuilder : IEndpointConventionBuilder =>
        endpoints.WithMetadata(new UserRateLimit());

    private static RateLimitingOptions Options(HttpContext context) => context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> PerUser(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
        });

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? $"user:{userId}" : $"ip:{context.Connection.RemoteIpAddress}";

    private sealed class UserRateLimit;

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
