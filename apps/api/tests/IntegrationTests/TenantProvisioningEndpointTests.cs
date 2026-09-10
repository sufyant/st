using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisioningEndpointTests
{
    [Fact]
    public async Task PostTenant_QueuesProvisioningAndReturnsAccepted()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Provisioning", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostTenant_WritesTheTenantAndTheMessageTogether()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await fixture.CountControlPlaneRowsAsync("control.tenants"));
        Assert.Equal(1, await fixture.CountControlPlaneRowsAsync("control.outbox_messages"));
    }

    [Fact]
    public async Task PostTenant_ForADuplicateAlias_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var first = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostTenant_ForAReservedAlias_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "admin" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTenant_ForANonPlatformAdmin_ReturnsForbidden()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenant_ReportsProvisioningProgress()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var created = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        var location = created.Headers.Location!.ToString();

        // Act
        using var response = await fixture.Client.GetAsync(location, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("acme", body.GetProperty("alias").GetString());
        Assert.Equal("CreatingDatabase", body.GetProperty("provisioningStep").GetString());
    }

    [Fact]
    public async Task GetTenant_ForAnUnknownIdentifier_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.GetAsync(
            $"/admin/api/v1/tenants/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetryProvisioning_QueuesAnotherMessage()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var created = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();

        // Act
        using var response = await fixture.Client.PostAsync(
            $"/admin/api/v1/tenants/{id}/retry-provisioning",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, await fixture.CountControlPlaneRowsAsync("control.outbox_messages"));
    }
}
