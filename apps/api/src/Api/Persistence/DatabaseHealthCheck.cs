using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Api.Persistence;

// A pod is ready only while it reaches its database, and only as a role that row level security binds; the application does
// not start as any other role (DatabaseAccountCheck), and this keeps checking while it runs. Both of the application's connections
// are checked. A connection failure throws, and the health check service reports it unhealthy.
internal sealed class DatabaseHealthCheck(NpgsqlDataSource pooled, IWolverineRuntime messaging) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Without the setting Wolverine runs without message storage (ApiPipeline).
        if (messaging.Storage is not IMessageDatabase { DataSource: NpgsqlDataSource direct })
        {
            return HealthCheckResult.Unhealthy(
                $"ConnectionStrings:{PersistenceExtensions.DirectConnection} must name the application role's direct connection.");
        }

        string[] problems =
        [
            .. await DatabaseAccountCheck.ProblemsAsync(pooled, $"ConnectionStrings:{PersistenceExtensions.PooledConnection}", cancellationToken),
            .. await DatabaseAccountCheck.ProblemsAsync(direct, $"ConnectionStrings:{PersistenceExtensions.DirectConnection}", cancellationToken),
        ];

        return problems.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Row level security would not hold: {string.Join(", ", problems)}.");
    }
}
