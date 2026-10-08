using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Api.Persistence;

// A pod is ready only while it reaches its database, and only as a role that row level security binds: not
// a superuser, without BYPASSRLS, and owner of no table, since an owner is not subject to the policies of its tables. Both of the
// application's connections are checked: the pooled one requests use, and the direct one Wolverine keeps its messages over.
// A connection failure throws, and the health check service reports it unhealthy.
internal sealed class DatabaseHealthCheck(NpgsqlDataSource pooled, IWolverineRuntime messaging) : IHealthCheck
{
    private const string RoleQuery =
        """
        SELECT
            role.rolsuper,
            role.rolbypassrls,
            EXISTS (SELECT FROM pg_class WHERE relowner = role.oid AND relkind IN ('r', 'p'))
        FROM pg_roles AS role
        WHERE role.rolname = current_user
        """;

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
            .. await ProblemsAsync(pooled, PersistenceExtensions.PooledConnection, cancellationToken),
            .. await ProblemsAsync(direct, PersistenceExtensions.DirectConnection, cancellationToken),
        ];

        return problems.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Row level security would not hold: {string.Join(", ", problems)}.");
    }

    private static async Task<string[]> ProblemsAsync(NpgsqlDataSource dataSource, string connection, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RoleQuery);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return
        [
            .. reader.GetBoolean(0) ? [$"the {connection} connection's role is a superuser"] : Array.Empty<string>(),
            .. reader.GetBoolean(1) ? [$"the {connection} connection's role bypasses row level security"] : Array.Empty<string>(),
            .. reader.GetBoolean(2) ? [$"the {connection} connection's role owns tables"] : Array.Empty<string>(),
        ];
    }
}
