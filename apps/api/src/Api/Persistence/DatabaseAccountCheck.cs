using Npgsql;

namespace Api.Persistence;

// R10: the application does not start as a role that row level security does not bind: a superuser, a role that bypasses it, or
// the owner of a table, who can switch it off. A member of such a role, directly or through other roles, has its rights or can
// become it (PostgreSQL documentation, Privileges), so the role and every role it is a member of are checked. Both of the
// application's connections are checked: the one requests use, and the
// one Wolverine keeps its messages over, when it has one of its own. It runs before Wolverine starts. The migration step never starts
// the host, so it runs as the owner without this check.
internal sealed class DatabaseAccountCheck(IConfiguration configuration) : IHostedService
{
    // The connection's own role, and every role it is a member of.
    private const string RoleQuery =
        """
        SELECT
            role.rolname,
            role.rolname = current_user,
            role.rolsuper,
            role.rolbypassrls,
            EXISTS (SELECT FROM pg_class WHERE relowner = role.oid AND relkind IN ('r', 'p'))
        FROM pg_roles AS role
        WHERE pg_has_role(current_user, role.oid, 'MEMBER')
        ORDER BY role.rolname = current_user DESC, role.rolname
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

        List<string> problems = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetBoolean(1) ? $"the {connection} connection's role" : $"the {connection} connection's role is a member of {reader.GetString(0)}, which";
            problems.AddRange([
                .. reader.GetBoolean(2) ? [$"{role} is a superuser"] : Array.Empty<string>(),
                .. reader.GetBoolean(3) ? [$"{role} bypasses row level security"] : Array.Empty<string>(),
                .. reader.GetBoolean(4) ? [$"{role} owns tables"] : Array.Empty<string>(),
            ]);
        }

        return [.. problems];
    }

    private static async Task<string[]> ProblemsAsync(string key, string connectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        return await ProblemsAsync(dataSource, key, cancellationToken);
    }
}
