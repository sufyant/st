using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantMigrationRunnerTests
{
    private static readonly AuditInterceptor auditInterceptor = new(TimeProvider.System);

    [Fact]
    public async Task RunAsync_MigratesActiveTenantsAndSkipsTheRest()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using var controlPlaneContext = await CreateControlPlaneAsync(connectionString);
        var active = Tenant.Create(TenantAlias.Create("acme"));
        active.CompleteProvisioning();
        var provisioning = Tenant.Create(TenantAlias.Create("globex"));
        controlPlaneContext.Tenants.AddRange(active, provisioning);
        await controlPlaneContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await TenantSchemaMigratorTests.ExecuteAsync(
            connectionString,
            $"CREATE DATABASE {active.DatabaseName.Value}");
        var runner = new TenantMigrationRunner(
            controlPlaneContext,
            new TenantSchemaMigrator(new TenantDbContextFactory(connectionString, auditInterceptor)));

        // Act
        var outcomes = await runner.RunAsync(null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TenantMigrationRunner.Migrated, Assert.Single(outcomes, x => x.Alias == "acme").Result);
        Assert.Equal(TenantMigrationRunner.Skipped, Assert.Single(outcomes, x => x.Alias == "globex").Result);
        Assert.False(await TenantSchemaMigratorTests.DatabaseExistsAsync(
            connectionString,
            provisioning.DatabaseName.Value));
    }

    [Fact]
    public async Task RunAsync_ForAnActiveTenantWithoutADatabase_ReportsFailure()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using var controlPlaneContext = await CreateControlPlaneAsync(connectionString);
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.CompleteProvisioning();
        controlPlaneContext.Tenants.Add(tenant);
        await controlPlaneContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var runner = new TenantMigrationRunner(
            controlPlaneContext,
            new TenantSchemaMigrator(new TenantDbContextFactory(connectionString, auditInterceptor)));

        // Act
        var outcomes = await runner.RunAsync(null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TenantMigrationRunner.Failed, Assert.Single(outcomes).Result);
        Assert.False(await TenantSchemaMigratorTests.DatabaseExistsAsync(
            connectionString,
            tenant.DatabaseName.Value));
    }

    private static async Task<ControlPlaneDbContext> CreateControlPlaneAsync(string connectionString)
    {
        await TenantSchemaMigratorTests.ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "control_plane" };
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(builder.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        var context = new ControlPlaneDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return context;
    }
}
