using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.TenantIsolation;

[Collection(nameof(PostgresCollection))]
public sealed class SchemaIsolationTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task TwoProvisionedTenants_GetTwoDistinctPostgresSchemas()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);

        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-b-{Guid.NewGuid():N}"[..15]), "Tenant B");

        // Act
        // Query by Tenant.SchemaName (not a hand-rolled "tenant_{slug}" string): the slug
        // itself may contain hyphens, but SchemaName replaces '-' with '_' before prefixing,
        // so the two must not be assumed identical.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_name FROM information_schema.schemata WHERE schema_name = ANY(@names)";
        command.Parameters.AddWithValue("names", new[] { tenantA.SchemaName, tenantB.SchemaName });
        var foundSchemas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foundSchemas.Add(reader.GetString(0));
        }

        // Assert
        Assert.Equal(2, foundSchemas.Count);
        Assert.NotEqual(foundSchemas[0], foundSchemas[1]);
    }
}
