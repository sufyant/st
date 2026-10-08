using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Email;

// Outside Development email goes only through Resend. Without its settings no email could leave, invitations included,
// so the pod is not ready until they are there.
internal sealed class EmailChannelHealthCheck(IHostEnvironment environment, IOptions<ResendOptions> resend) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(environment.IsDevelopment() || resend.Value.IsConfigured
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{ResendOptions.Section}:ApiKey and {ResendOptions.Section}:From must be set outside Development, or no email is sent."));
}
