using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Tenants;
using Npgsql;

namespace Infrastructure.Provisioning;

public sealed class TenantProvisioner(string connectionString, AuditInterceptor auditInterceptor)
{
    public async Task CreateDatabaseAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)",
            connection);
        existsCommand.Parameters.AddWithValue("name", databaseName.Value);

        if (!(bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!)
        {
            await using var createCommand = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName.Value}\"",
                connection);
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // PostgreSQL grants CONNECT to PUBLIC on every new database, which would let any tenant's
        // dynamic role open a session against every other tenant's database. The revoke runs on
        // every call so that a database created before this step was introduced is repaired too.
        await using var revokeCommand = new NpgsqlCommand(
            $"REVOKE CONNECT ON DATABASE \"{databaseName.Value}\" FROM PUBLIC",
            connection);
        await revokeCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MigrateSchemaAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString, auditInterceptor));

        return migrator.MigrateAsync(databaseName.Value, cancellationToken);
    }

    public async Task<string> GrantTenantAccessAsync(
        TenantDatabaseName databaseName,
        TenantRoleName roleName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(roleName);

        var password = GeneratePassword();

        await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
        await maintenance.OpenAsync(cancellationToken);
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)",
            maintenance);
        existsCommand.Parameters.AddWithValue("name", roleName.Value);
        var roleExists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!;

        await using var roleCommand = new NpgsqlCommand(
            roleExists
                ? $"ALTER ROLE \"{roleName.Value}\" PASSWORD '{password}'"
                : $"CREATE ROLE \"{roleName.Value}\" LOGIN PASSWORD '{password}'",
            maintenance);
        await roleCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var connectCommand = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE \"{databaseName.Value}\" TO \"{roleName.Value}\"",
            maintenance);
        await connectCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var tenant = new NpgsqlConnection(TenantConnectionString(databaseName));
        await tenant.OpenAsync(cancellationToken);
        await using var grantCommand = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA public TO "{roleName.Value}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{roleName.Value}";
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{roleName.Value}";
            """,
            tenant);
        await grantCommand.ExecuteNonQueryAsync(cancellationToken);

        return password;
    }

    public async Task DropTenantAsync(
        TenantDatabaseName databaseName,
        TenantRoleName roleName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(roleName);

        await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
        await maintenance.OpenAsync(cancellationToken);
        await using var dropDatabaseCommand = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName.Value}\" WITH (FORCE)",
            maintenance);
        await dropDatabaseCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var dropRoleCommand = new NpgsqlCommand(
            $"DROP ROLE IF EXISTS \"{roleName.Value}\"",
            maintenance);
        await dropRoleCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public TenantDbContext CreateTenantDbContext(TenantDatabaseName databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        return new TenantDbContextFactory(connectionString, auditInterceptor).Create(databaseName.Value);
    }

    private string MaintenanceConnectionString() =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;

    private string TenantConnectionString(TenantDatabaseName databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName.Value }.ConnectionString;
}
