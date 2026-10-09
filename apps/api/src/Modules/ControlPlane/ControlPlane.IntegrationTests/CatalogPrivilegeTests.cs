using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

public sealed class CatalogPrivilegeTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadTableOwners_CatalogSchema_AreAllTheOwnerRole()
    {
        var owners = await QueryAsync<string>(
            DatabaseRoles.Application, "SELECT DISTINCT tableowner FROM pg_tables WHERE schemaname = 'catalog'");

        owners.ShouldBe([DatabaseRoles.Owner]);
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
