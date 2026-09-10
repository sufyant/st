using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task Post_WithAMalformedBody_ReturnsAProblemDocument()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.Client.PostAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            body,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty("title", out _));
        Assert.True(document.RootElement.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Get_ForAnUnknownRoute_ReturnsNotFoundWithoutLeakingATrace()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        var response = await fixture.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/nowhere",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAnInvalidEmail_ReturnsFieldLevelValidationErrors()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        var response = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = "not-an-email", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
    }
}
