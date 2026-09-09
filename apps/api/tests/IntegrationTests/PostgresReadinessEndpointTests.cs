using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class PostgresReadinessEndpointTests
{
    [Fact]
    public async Task GetReadiness_ReturnsOkWhenPostgresAcceptsConnections()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Postgres",
                postgres.GetConnectionString()));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
