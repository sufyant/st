using Npgsql;
using Api.Tests.Shared;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class PostgresConnectivityTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Container_IsReachable_ExecutesSimpleQuery()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        // Act
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync();

        // Assert
        Assert.Equal(1, result);
    }
}
