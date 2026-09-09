using Infrastructure.Persistence.Tenants;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantSchemaProvisionerTests
{
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
}
