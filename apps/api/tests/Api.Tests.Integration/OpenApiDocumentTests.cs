using System.Net;
using System.Text.Json;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class OpenApiDocumentTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetOpenApiDocument_ReturnsValidJsonDescribingKnownEndpoints()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/tenant", out _));
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/whoami", out _));
    }
}
