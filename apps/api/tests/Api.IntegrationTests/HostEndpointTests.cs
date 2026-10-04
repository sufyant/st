using System.Net;
using Microsoft.Extensions.Hosting;
using Tenancy;

namespace Api.IntegrationTests;

public sealed class HostEndpointTests(Database database) : IAsyncLifetime
{
    private ApiFactory _api = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        _client = _api.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_its_database_the_application_is_live_but_not_ready()
    {
        await using var api = new ApiFactory("Host=127.0.0.1;Port=1;Username=nobody;Password=none;Timeout=1");
        var client = api.CreateClient();

        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Row level security does not hold for a superuser, a role with BYPASSRLS or a table's owner, so a pod connected as one must
    // never receive traffic (0014, 0018).
    [Fact]
    public async Task Connected_as_the_owner_the_application_is_not_ready()
    {
        var ready = await ReadyStatusAsync(database.OwnerConnectionString);

        ready.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Connected_as_a_superuser_the_application_is_not_ready()
    {
        var ready = await ReadyStatusAsync(database.SuperuserConnectionString);

        ready.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Connected_as_a_role_that_bypasses_row_level_security_the_application_is_not_ready()
    {
        var bypassing = await database.CreateLoginRoleAsync("BYPASSRLS");

        var ready = await ReadyStatusAsync(bypassing);

        ready.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Outside Development the API must know which clients may use it; without the list any origin's token would be accepted (0028).
    [Fact]
    public async Task Outside_development_the_application_is_not_ready_without_authorized_parties()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, environment: Environments.Production, authorizedParties: []);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Outside_development_the_application_is_ready_with_authorized_parties()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, environment: Environments.Production);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task In_development_the_application_is_ready_without_authorized_parties()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, authorizedParties: []);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_route_returns_problem_details()
    {
        var response = await _client.GetAsync("/v1/no-such-route", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private static async Task<HttpStatusCode> ReadyStatusAsync(string pooledConnectionString)
    {
        await using var api = new ApiFactory(pooledConnectionString);
        var response = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    [Fact]
    public async Task The_openapi_document_is_served_in_development()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_scalar_reference_is_served_in_development()
    {
        var response = await _client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
