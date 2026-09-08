using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantMigrationRunnerTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task MigrateTenantAsync_CreatesMigrationsHistoryTableInTenantSchema()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var slug = TenantSlug.Create($"mig-{Guid.NewGuid():N}"[..15]);
        var tenant = await provisioningService.ProvisionAsync(slug, "Migration Test Tenant");
        var runner = new TenantMigrationRunner(dbContext, fixture.ConnectionString);

        // Act
        await runner.MigrateTenantAsync(tenant.SchemaName);

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schemaName AND table_name = '__EFMigrationsHistory'",
            connection);
        command.Parameters.AddWithValue("schemaName", tenant.SchemaName);
        var tableCount = (long)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, tableCount);
    }

    [Fact]
    public async Task MigrateAllTenantsAsync_MigratesEveryProvisionedTenant()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var firstTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"all-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var secondTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"all-b-{Guid.NewGuid():N}"[..15]), "Tenant B");
        var runner = new TenantMigrationRunner(dbContext, fixture.ConnectionString);

        // Act
        await runner.MigrateAllTenantsAsync();

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        foreach (var schemaName in new[] { firstTenant.SchemaName, secondTenant.SchemaName })
        {
            await using var command = new NpgsqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schemaName AND table_name = '__EFMigrationsHistory'",
                connection);
            command.Parameters.AddWithValue("schemaName", schemaName);
            var tableCount = (long)(await command.ExecuteScalarAsync())!;
            Assert.Equal(1, tableCount);
        }
    }

    [Fact]
    public async Task MigrateTenantAsync_UnsafeSchemaName_ThrowsArgumentException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var runner = new TenantMigrationRunner(dbContext, fixture.ConnectionString);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => runner.MigrateTenantAsync("Bad Schema"));
    }
}
