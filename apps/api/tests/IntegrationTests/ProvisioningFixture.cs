using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class ProvisioningFixture : IAsyncDisposable
{
    public const string OwnerExternalUserId = "user_2abc123";

    private readonly PostgreSqlContainer postgres;

    private ProvisioningFixture(PostgreSqlContainer postgres, string controlPlaneConnectionString)
    {
        this.postgres = postgres;
        ControlPlaneConnectionString = controlPlaneConnectionString;
        Provisioner = new TenantProvisioner(postgres.GetConnectionString());
    }

    public string ControlPlaneConnectionString { get; }

    public TenantProvisioner Provisioner { get; }

    public string ServerConnectionString => postgres.GetConnectionString();

    public static async Task<ProvisioningFixture> StartAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE ROLE st_tenant LOGIN PASSWORD 'test'");
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");

        await using (var context = CreateControlPlaneDbContext(controlPlane))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new ProvisioningFixture(postgres, controlPlane);
    }

    public ControlPlaneDbContext CreateControlPlane() => CreateControlPlaneDbContext(ControlPlaneConnectionString);

    public async Task<Tenant> AddProvisioningTenantAsync(string alias)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), TenantAlias.Create(alias), DateTimeOffset.UtcNow);
        await using var context = CreateControlPlane();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return tenant;
    }

    public async Task<Tenant> ReloadAsync(Guid tenantId)
    {
        await using var context = CreateControlPlane();

        return await context.Tenants.SingleAsync(
            tenant => tenant.Id == tenantId,
            TestContext.Current.CancellationToken);
    }

    public async Task<long> CountAsync(string databaseName, string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    private static ControlPlaneDbContext CreateControlPlaneDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        return new ControlPlaneDbContext(options);
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
