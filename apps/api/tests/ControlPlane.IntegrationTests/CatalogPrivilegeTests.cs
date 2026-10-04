using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

public sealed class CatalogPrivilegeTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_owns_every_catalog_table()
    {
        var owners = await QueryAsync<string>(
            DatabaseRoles.Application, "SELECT DISTINCT tableowner FROM pg_tables WHERE schemaname = 'catalog'");

        owners.ShouldBe([DatabaseRoles.Owner]);
    }

    [Fact]
    public async Task The_reporting_role_reads_the_catalog()
    {
        var read = () => QueryAsync<long>(DatabaseRoles.Reporting, "SELECT count(*) FROM catalog.tenants");

        await read.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task The_reporting_role_cannot_write_the_catalog()
    {
        var write = () => QueryAsync<long>(
            DatabaseRoles.Reporting, "INSERT INTO catalog.users (id, external_id) VALUES (gen_random_uuid(), 'user_reporting') RETURNING 1");

        var failure = await write.ShouldThrowAsync<PostgresException>();
        failure.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<List<T>> QueryAsync<T>(string role, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(role));
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        List<T> rows = [];
        while (await reader.ReadAsync(Cancellation))
        {
            rows.Add(reader.GetFieldValue<T>(0));
        }

        return rows;
    }
}
