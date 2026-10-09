using Npgsql;

namespace Api.IntegrationTests;

// Every ordinary table of the database, read from pg_class rather than from any model, so a table that no module maps is found
// too. A table on the list of tables without a tenant needs nothing; every other one is a tenant table and must have row level
// security enabled and forced, with the tenant isolation policy limiting reads and writes to the declared tenant (R1, R6).
internal sealed class DatabaseTables(Database database)
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

    public async Task<List<string>> TenantTablesAsync() =>
        [.. (await ReadAsync()).Where(table => !TablesWithoutTenant.Contains(table.Schema, table.Name)).Select(table => table.QualifiedName)];

    public async Task<List<string>> GapsAsync()
    {
        var tables = await ReadAsync();
        var found = tables.Select(table => table.QualifiedName).ToHashSet();

        return
        [
            .. TablesWithoutTenant.Tables.Where(listed => !found.Contains(listed)).Select(listed => $"{listed}: listed, but not in the database"),
            .. tables.Where(table => !TablesWithoutTenant.Contains(table.Schema, table.Name)).SelectMany(Gaps),
        ];
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

    private async Task<List<Table>> ReadAsync()
    {
        await using var connection = new NpgsqlConnection(database.SuperuserConnectionString);
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
