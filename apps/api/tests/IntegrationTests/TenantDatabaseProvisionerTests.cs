using Domain.Tenants;
using Infrastructure.Persistence.Admin;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantDatabaseProvisionerTests
{
    [Fact]
    public async Task MigrateAsync_AppliesTenantMigrationsForEverySystemDatabaseTenant()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await CreateDatabaseAsync(connectionString, "systemdb");
        var systemDatabaseConnectionString = WithDatabase(connectionString, "systemdb");
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(systemDatabaseConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"))
            .Options;
        await using var adminContext = new AdminDbContext(options);
        await adminContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var firstTenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        var secondTenant = Tenant.Create(Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce"), TenantAlias.Create("globex"));
        adminContext.Tenants.AddRange(firstTenant, secondTenant);
        await adminContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var migrator = new PostgresTenantDatabaseMigrator(
            adminContext,
            new PostgresTenantDatabaseProvisioner(dataSource),
            new TenantDbContextFactory(connectionString));

        // Act
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await HasUsersTableAsync(connectionString, firstTenant.DatabaseName));
        Assert.True(await HasUsersTableAsync(connectionString, secondTenant.DatabaseName));
    }

    [Fact]
    public async Task TenantData_CannotBeReadFromAnotherTenantDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var firstTenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        var secondTenant = Tenant.Create(Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce"), TenantAlias.Create("globex"));
        var provisioner = new PostgresTenantDatabaseProvisioner(dataSource);
        var contextFactory = new TenantDbContextFactory(connectionString);
        await provisioner.CreateAsync(firstTenant, TestContext.Current.CancellationToken);
        await provisioner.CreateAsync(secondTenant, TestContext.Current.CancellationToken);
        await using var firstTenantContext = contextFactory.Create(firstTenant);
        await using var secondTenantContext = contextFactory.Create(secondTenant);
        await firstTenantContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await secondTenantContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var userId = Guid.Parse("c9f47e68-86a2-4e18-a51c-46afce941d95");
        await firstTenantContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO users (id, external_user_id, status) VALUES ({userId}, {"user_2abc123"}, {"Active"})",
            TestContext.Current.CancellationToken);

        // Act
        var firstTenantUser = await firstTenantContext.Users.SingleAsync(TestContext.Current.CancellationToken);
        var secondTenantUsers = await secondTenantContext.Users.ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("user_2abc123", firstTenantUser.ExternalUserId.Value);
        Assert.Empty(secondTenantUsers);
    }

    [Fact]
    public async Task CreateAsync_CreatesSeparateDatabasesForEachTenant()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        var provisioner = new PostgresTenantDatabaseProvisioner(dataSource);
        var firstTenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        var secondTenant = Tenant.Create(Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce"), TenantAlias.Create("globex"));

        // Act
        await provisioner.CreateAsync(firstTenant, TestContext.Current.CancellationToken);
        await provisioner.CreateAsync(secondTenant, TestContext.Current.CancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE datname IN (@first, @second)",
            connection);
        command.Parameters.AddWithValue("first", firstTenant.DatabaseName);
        command.Parameters.AddWithValue("second", secondTenant.DatabaseName);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var databases = new HashSet<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            databases.Add(reader.GetString(0));
        }

        // Assert
        Assert.Contains(firstTenant.DatabaseName, databases);
        Assert.Contains(secondTenant.DatabaseName, databases);
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> HasUsersTableAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(WithDatabase(connectionString, databaseName));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return result is string { } tableName && tableName == "users";
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;
}
