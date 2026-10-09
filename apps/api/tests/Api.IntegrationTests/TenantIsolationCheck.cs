using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Tenancy;

namespace Api.IntegrationTests;

// Finds every ITenantEntity of every module database, with no list to keep up to date, and reports what it lacks: row level
// security enabled and forced, exactly one policy limiting reads and writes to the active tenant, and the tenant column default.
internal sealed class TenantIsolationCheck(Database database)
{
    public static List<IEntityType> TenantEntities(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        return
        [
            .. services.GetServices<IModuleMigrator>()
                .Select(migrator => (DbContext)scope.ServiceProvider.GetRequiredService(migrator.DbContextType))
                .SelectMany(context => context.Model.GetEntityTypes())
                .Where(entity => typeof(ITenantEntity).IsAssignableFrom(entity.ClrType)),
        ];
    }

    public async Task<List<string>> GapsAsync(IServiceProvider services)
    {
        List<string> gaps = [];
        foreach (var entity in TenantEntities(services))
        {
            gaps.AddRange(await DatabaseGapsAsync(entity.GetSchema()!, entity.GetTableName()!));
        }

        return gaps;
    }

    private async Task<List<string>> DatabaseGapsAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                c.relrowsecurity,
                c.relforcerowsecurity,
                (SELECT count(*) FROM pg_policies p WHERE p.schemaname = @schema AND p.tablename = @table),
                (SELECT count(*) FROM pg_policies p
                    WHERE p.schemaname = @schema AND p.tablename = @table AND p.cmd = 'ALL' AND p.permissive = 'PERMISSIVE'
                    AND p.qual = p.with_check AND p.qual LIKE '%tenant_id = %current_setting(''app.tenant_id''%'),
                (SELECT pg_get_expr(d.adbin, d.adrelid) FROM pg_attrdef d
                    JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum
                    WHERE d.adrelid = c.oid AND a.attname = 'tenant_id')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        if (!await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            return [$"{schema}.{table}: table not found"];
        }

        List<string> gaps = [];
        if (!reader.GetBoolean(0))
        {
            gaps.Add($"{schema}.{table}: row level security is not enabled");
        }

        if (!reader.GetBoolean(1))
        {
            gaps.Add($"{schema}.{table}: row level security is not forced");
        }

        if (reader.GetInt64(2) != 1 || reader.GetInt64(3) != 1)
        {
            gaps.Add($"{schema}.{table}: expected exactly one policy limiting reads and writes to the active tenant");
        }

        if (reader.IsDBNull(4) || !reader.GetString(4).Contains("current_setting('app.tenant_id'", StringComparison.Ordinal))
        {
            gaps.Add($"{schema}.{table}: the tenant column does not default to the active tenant");
        }

        return gaps;
    }
}
