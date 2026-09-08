using System.Linq;
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
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/tenant", out var tenantPath));
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/whoami", out var whoamiPath));

        AssertHasRequiredTenantAliasParameter(tenantPath, "put");
        AssertHasRequiredTenantAliasParameter(whoamiPath, "get");
    }

    private static void AssertHasRequiredTenantAliasParameter(JsonElement pathItem, string httpMethod)
    {
        var operation = pathItem.GetProperty(httpMethod);
        Assert.True(
            operation.TryGetProperty("parameters", out var parameters),
            $"Expected a 'parameters' array on the {httpMethod} operation.");

        var tenantAliasParameter = parameters.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("name").GetString() == "tenant-alias");

        Assert.True(
            tenantAliasParameter.ValueKind != JsonValueKind.Undefined,
            $"Expected a 'tenant-alias' parameter on the {httpMethod} operation.");
        Assert.Equal("path", tenantAliasParameter.GetProperty("in").GetString());
        Assert.True(tenantAliasParameter.GetProperty("required").GetBoolean());
    }
}
