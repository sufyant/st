using Domain.Tenants;
using Infrastructure.Persistence.Admin;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantSchemaProvisionerTests
{
    [Fact]
    public async Task MigrateAsync_AppliesTenantMigrationsForEveryControlPlaneTenant()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"))
            .Options;
        await using var adminContext = new AdminDbContext(options);
        await adminContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var firstTenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        var secondTenant = Tenant.Create(Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce"), TenantAlias.Create("globex"));
        adminContext.Tenants.AddRange(firstTenant, secondTenant);
        await adminContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        var migrator = new PostgresTenantSchemaMigrator(
            adminContext,
            new PostgresTenantSchemaProvisioner(dataSource),
            new TenantDbContextFactory(postgres.GetConnectionString()));

        // Act
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var firstTenantHasUsersTable = await HasUsersTableAsync(connection, firstTenant.SchemaName);
        var secondTenantHasUsersTable = await HasUsersTableAsync(connection, secondTenant.SchemaName);

        // Assert
        Assert.True(firstTenantHasUsersTable);
        Assert.True(secondTenantHasUsersTable);
    }

    [Fact]
    public void Create_WithEmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var factory = new TenantDbContextFactory("Host=localhost;Database=st");

        // Act
        var action = () => factory.Create(Guid.Empty);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public async Task TenantData_CannotBeReadFromAnotherTenantSchema()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        var firstTenantId = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
        var secondTenantId = Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce");
        var provisioner = new PostgresTenantSchemaProvisioner(dataSource);
        var contextFactory = new TenantDbContextFactory(postgres.GetConnectionString());
        await provisioner.CreateAsync(firstTenantId, TestContext.Current.CancellationToken);
        await provisioner.CreateAsync(secondTenantId, TestContext.Current.CancellationToken);
        await using var firstTenantContext = contextFactory.Create(firstTenantId);
        await using var secondTenantContext = contextFactory.Create(secondTenantId);
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
    public async Task CreateAsync_CreatesSeparateSchemasForEachTenant()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        var provisioner = new PostgresTenantSchemaProvisioner(dataSource);
        var firstTenantId = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
        var secondTenantId = Guid.Parse("b31ee8a6-6411-4aa5-a5a9-1e6bbf92f2ce");

        // Act
        await provisioner.CreateAsync(firstTenantId, TestContext.Current.CancellationToken);
        await provisioner.CreateAsync(secondTenantId, TestContext.Current.CancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT nspname FROM pg_namespace WHERE nspname IN (@first, @second)",
            connection);
        command.Parameters.AddWithValue("first", firstTenantId.ToString("N"));
        command.Parameters.AddWithValue("second", secondTenantId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var schemas = new HashSet<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        // Assert
        Assert.Contains(firstTenantId.ToString("N"), schemas);
        Assert.Contains(secondTenantId.ToString("N"), schemas);
    }

    private static async Task<bool> HasUsersTableAsync(NpgsqlConnection connection, string schemaName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = 'users')",
            connection);
        command.Parameters.AddWithValue("schema", schemaName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
