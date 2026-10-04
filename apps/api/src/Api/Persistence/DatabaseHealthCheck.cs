using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Api.Persistence;

// A pod is ready only while it reaches its database (0038), and only as a role that row level security binds (0014, 0018): not
// a superuser, without BYPASSRLS, and owner of no table, since an owner is not subject to the policies of its tables. A
// connection failure throws, and the health check service reports it unhealthy.
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
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
        await using var command = dataSource.CreateCommand(RoleQuery);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        string[] problems =
        [
            .. reader.GetBoolean(0) ? ["the database role is a superuser"] : Array.Empty<string>(),
            .. reader.GetBoolean(1) ? ["the database role bypasses row level security"] : Array.Empty<string>(),
            .. reader.GetBoolean(2) ? ["the database role owns tables"] : Array.Empty<string>(),
        ];

        return problems.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Row level security would not hold: {string.Join(", ", problems)}.");
    }
}
