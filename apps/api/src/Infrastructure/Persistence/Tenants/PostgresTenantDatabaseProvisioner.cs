using Domain.Tenants;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class PostgresTenantDatabaseProvisioner(NpgsqlDataSource dataSource)
{
    public async Task CreateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @databaseName)",
            connection);
        existsCommand.Parameters.AddWithValue("databaseName", tenant.DatabaseName);

        if ((bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!)
        {
            return;
        }

        await using var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{tenant.DatabaseName}\"", connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
