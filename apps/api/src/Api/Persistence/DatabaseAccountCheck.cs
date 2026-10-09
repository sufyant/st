using Npgsql;

namespace Api.Persistence;

// R10: the application does not start as a role that row level security does not bind: a superuser, a role that bypasses it, or
// the owner of a table, who can switch it off. Both of the application's connections are checked: the one requests use, and the
// one Wolverine keeps its messages over, when it has one of its own. It runs before Wolverine starts. The migration step never starts
// the host, so it runs as the owner without this check.
internal sealed class DatabaseAccountCheck(IConfiguration configuration) : IHostedService
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string[] problems =
        [
            .. await ProblemsAsync(PersistenceExtensions.DatabaseKey, configuration.GetConnectionString(PersistenceExtensions.DatabaseConnection)!, cancellationToken),
            .. PersistenceExtensions.MessagingConnectionOf(configuration) is { Key: var key, ConnectionString: var messaging } && key != PersistenceExtensions.DatabaseKey
                ? await ProblemsAsync(key, messaging, cancellationToken)
                : [],
        ];

        if (problems.Length > 0)
        {
            throw new InvalidOperationException($"The application does not start: {string.Join("; ", problems)}.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The readiness check asks the same while the application runs, since a role can change.
    public static async Task<string[]> ProblemsAsync(NpgsqlDataSource dataSource, string connection, CancellationToken cancellationToken)
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

    private static async Task<string[]> ProblemsAsync(string key, string connectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        return await ProblemsAsync(dataSource, key, cancellationToken);
    }
}
