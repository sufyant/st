using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Api.Persistence;

// A pod is ready only while it reaches its database, and only as a role that row level security binds; the application does
// not start as any other role (DatabaseAccountCheck), and this keeps checking while it runs. Both of the application's connections
// are checked. A connection failure throws, and the health check service reports it unhealthy.
internal sealed class DatabaseHealthCheck(NpgsqlDataSource database, IWolverineRuntime messaging, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var messages = (NpgsqlDataSource)((IMessageDatabase)messaging.Storage).DataSource;
        string[] problems =
        [
            .. await DatabaseAccountCheck.ProblemsAsync(database, PersistenceExtensions.DatabaseKey, cancellationToken),
            .. await DatabaseAccountCheck.ProblemsAsync(messages, PersistenceExtensions.MessagingConnectionOf(configuration)!.Value.Key, cancellationToken),
        ];

        return problems.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Row level security would not hold: {string.Join(", ", problems.Distinct())}.");
    }
}
