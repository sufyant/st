using System.Reflection;
using Npgsql;

namespace Api.Persistence;

// R10: the application does not start as a role that row level security does not bind: a superuser, a role that bypasses it, or
// the owner of a table, who can switch it off. Both of the application's connections are checked: the pooled one requests use,
// and the direct one Wolverine keeps its messages over. It runs before Wolverine starts. The migration step never starts the
// host, so it runs as the owner without this check.
internal sealed class DatabaseAccountCheck(IConfiguration configuration) : IHostedService
{
    // The build writes the OpenAPI document with the GetDocument.Insider tool, which runs Program and starts the host without any
    // configuration and on a server that serves no request. That process is the only one this check lets through.
    private const string OpenApiDocumentTool = "GetDocument.Insider";

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
        if (Assembly.GetEntryAssembly()?.GetName().Name == OpenApiDocumentTool)
        {
            return;
        }

        string[] problems =
        [
            .. await ProblemsAsync(PersistenceExtensions.PooledConnection, cancellationToken),
            .. await ProblemsAsync(PersistenceExtensions.DirectConnection, cancellationToken),
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

    private async Task<string[]> ProblemsAsync(string connection, CancellationToken cancellationToken)
    {
        var setting = $"ConnectionStrings:{connection}";
        if (configuration.GetConnectionString(connection) is not { Length: > 0 } connectionString)
        {
            return [$"{setting} must name the application role's connection"];
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        return await ProblemsAsync(dataSource, setting, cancellationToken);
    }
}
