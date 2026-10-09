using Npgsql;

namespace Api.IntegrationTests;

// Every ordinary table of the database, read from pg_class rather than from any model, so a table that no module maps is found
// too. A table on the list of tables without a tenant needs nothing; every other one is a tenant table and must have row level
// security enabled and forced, with the tenant isolation policy limiting reads and writes to the declared tenant (R1, R6).
// Every policy, read from pg_policy, is that policy on a tenant table or on the list of extra policies with its command (R11).
internal sealed class DatabaseTables(Database database, string? databaseName = null)
{
    private const string TablesQuery =
        """
        SELECT
            n.nspname,
            c.relname,
            c.relrowsecurity,
            c.relforcerowsecurity,
            EXISTS (
                SELECT FROM pg_policy p
                WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                    AND pg_get_expr(p.polqual, p.polrelid) = pg_get_expr(p.polwithcheck, p.polrelid)
                    AND pg_get_expr(p.polqual, p.polrelid) LIKE '%tenant_id = %current_setting(''app.tenant_id''%')
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
        ORDER BY n.nspname, c.relname
        """;

    private const string PoliciesQuery =
        """
        SELECT n.nspname, c.relname, p.polname,
            CASE p.polcmd WHEN 'r' THEN 'SELECT' WHEN 'a' THEN 'INSERT' WHEN 'w' THEN 'UPDATE' WHEN 'd' THEN 'DELETE' ELSE 'ALL' END
        FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        ORDER BY n.nspname, c.relname, p.polname
        """;

    public async Task<List<string>> TenantTablesAsync() =>
        [.. (await ReadAsync()).Where(table => !TablesWithoutTenant.Contains(table.Schema, table.Name)).Select(table => table.QualifiedName)];

    public async Task<List<string>> GapsAsync()
    {
        var tables = await ReadAsync();
        var found = tables.Select(table => table.QualifiedName).ToHashSet();
        var policies = await ReadPoliciesAsync();

        return
        [
            .. TablesWithoutTenant.Tables.Where(listed => !found.Contains(listed)).Select(listed => $"{listed}: listed, but not in the database"),
            .. tables.Where(table => !TablesWithoutTenant.Contains(table.Schema, table.Name)).SelectMany(Gaps),
            .. ExtraPolicies.Policies.Where(listed => !policies.Contains(listed))
                .Select(listed => $"{listed.Table}: policy {listed.Name} ({listed.Command}) listed, but not in the database"),
            .. policies.Where(policy => !IsExpected(policy))
                .Select(policy => $"{policy.Table}: policy {policy.Name} ({policy.Command}) is neither tenant isolation on a tenant table nor listed"),
        ];
    }

    private static bool IsExpected((string Table, string Name, string Command) policy)
    {
        var (schema, table) = (policy.Table.Split('.')[0], policy.Table.Split('.')[1]);
        var tenantIsolation = policy.Name == "tenant_isolation" && !TablesWithoutTenant.Contains(schema, table);

        return tenantIsolation || ExtraPolicies.Policies.Contains(policy);
    }

    private static IEnumerable<string> Gaps(Table table)
    {
        if (!table.RowLevelSecurity)
        {
            yield return $"{table.QualifiedName}: not on the list of tables without a tenant, and row level security is not enabled";
        }

        if (!table.Forced)
        {
            yield return $"{table.QualifiedName}: not on the list of tables without a tenant, and row level security is not forced";
        }

        if (!table.IsolationPolicy)
        {
            yield return $"{table.QualifiedName}: not on the list of tables without a tenant, and has no tenant isolation policy";
        }
    }

    private async Task<List<(string Table, string Name, string Command)>> ReadPoliciesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(PoliciesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        List<(string Table, string Name, string Command)> policies = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            policies.Add(($"{reader.GetString(0)}.{reader.GetString(1)}", reader.GetString(2), reader.GetString(3)));
        }

        return policies;
    }

    private string ConnectionString =>
        databaseName is null
            ? database.SuperuserConnectionString
            : new NpgsqlConnectionStringBuilder(database.SuperuserConnectionString) { Database = databaseName }.ConnectionString;

    private async Task<List<Table>> ReadAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(TablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        List<Table> tables = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(new Table(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        }

        return tables;
    }

    private sealed record Table(string Schema, string Name, bool RowLevelSecurity, bool Forced, bool IsolationPolicy)
    {
        public string QualifiedName => $"{Schema}.{Name}";
    }
}
