using Domain.Tenants;
using Infrastructure.Persistence.Tenants;
using Npgsql;

namespace Infrastructure.Provisioning;

public sealed class TenantProvisioner(string connectionString)
{
    private const string TenantRole = "st_tenant";

    public async Task CreateDatabaseAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)",
            connection);
        existsCommand.Parameters.AddWithValue("name", databaseName.Value);

        if ((bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!)
        {
            return;
        }

        await using var createCommand = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName.Value}\"",
            connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MigrateSchemaAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        return migrator.MigrateAsync(databaseName.Value, cancellationToken);
    }

    public async Task GrantTenantAccessAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
        await maintenance.OpenAsync(cancellationToken);
        await using var connectCommand = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE \"{databaseName.Value}\" TO {TenantRole}",
            maintenance);
        await connectCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var tenant = new NpgsqlConnection(TenantConnectionString(databaseName));
        await tenant.OpenAsync(cancellationToken);
        await using var grantCommand = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA public TO {TenantRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {TenantRole};
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {TenantRole};
            """,
            tenant);
        await grantCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public TenantDbContext CreateTenantDbContext(TenantDatabaseName databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        return new TenantDbContextFactory(connectionString).Create(databaseName.Value);
    }

    private string MaintenanceConnectionString() =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;

    private string TenantConnectionString(TenantDatabaseName databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName.Value }.ConnectionString;
}
