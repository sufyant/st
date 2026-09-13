using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence;
using Infrastructure.Provisioning;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisionerTests
{
    private static readonly AuditInterceptor AuditInterceptor = new(TimeProvider.System);

    private static readonly TenantDatabaseName DatabaseName = TenantDatabaseName.Create("tenant_acme");

    [Fact]
    public async Task CreateDatabaseAsync_CreatesTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task CreateDatabaseAsync_RunTwice_Succeeds()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task MigrateSchemaAsync_CreatesTheTenantTables()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await UsersTableExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task GrantTenantAccessAsync_CreatesADedicatedRoleThatCanReadTheTables()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
        var roleName = TenantRoleName.Create("access_acme");
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        var password = await provisioner.GrantTenantAccessAsync(
            DatabaseName, roleName, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(password));
        var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = DatabaseName.Value,
            Username = roleName.Value,
            Password = password
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(tenantConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GrantTenantAccessAsync_RunTwice_RotatesThePasswordAndStillGrantsAccess()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
        var roleName = TenantRoleName.Create("access_acme");
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);
        var firstPassword = await provisioner.GrantTenantAccessAsync(
            DatabaseName, roleName, TestContext.Current.CancellationToken);

        // Act
        var secondPassword = await provisioner.GrantTenantAccessAsync(
            DatabaseName, roleName, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(firstPassword, secondPassword);
        var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = DatabaseName.Value,
            Username = roleName.Value,
            Password = secondPassword
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(tenantConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> UsersTableExistsAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is "users";
    }
}
