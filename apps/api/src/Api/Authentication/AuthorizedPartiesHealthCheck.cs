using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Api.Authentication;

// Without a list of authorized parties the API accepts a session token issued to any origin of the Clerk instance. That
// is convenient in Development; anywhere else the pod is not ready until the list names the clients allowed to use the API.
internal sealed class AuthorizedPartiesHealthCheck(IHostEnvironment environment, IOptions<ClerkAuthenticationOptions> clerk) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(environment.IsDevelopment() || clerk.Value.AuthorizedParties.Count > 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{ClerkAuthentication.Section}:AuthorizedParties must list the origins of the clients allowed to use the API."));
}
