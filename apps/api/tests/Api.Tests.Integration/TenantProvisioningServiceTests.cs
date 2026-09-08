using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantProvisioningServiceTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task ProvisionAsync_CreatesTenantRowAndPostgresSchema()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var service = new TenantProvisioningService(dbContext);
        var slug = TenantSlug.Create($"prov-{Guid.NewGuid():N}"[..15]);

        // Act
        var tenant = await service.ProvisionAsync(slug, "Provisioning Test Tenant");

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @schemaName", connection);
        command.Parameters.AddWithValue("schemaName", tenant.SchemaName);
        var schemaCount = (long)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, schemaCount);
    }
}
