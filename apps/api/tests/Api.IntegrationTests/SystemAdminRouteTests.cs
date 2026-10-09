using System.Net;

namespace Api.IntegrationTests;

// The system door is a separate route group with its own authorization, and needs a second factor.
public sealed class SystemAdminRouteTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_system_admin_with_a_second_factor_reaches_the_system_routes()
    {
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin, secondFactor: true).GetAsync("/v1/system/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_system_admin_without_a_second_factor_is_forbidden()
    {
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin).GetAsync("/v1/system/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // Only the host's reading of fva may vouch for a second factor; a claim of that name inside the token proves nothing.
    [Fact]
    public async Task A_second_factor_claimed_by_the_token_itself_is_not_trusted()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var token = TestTokens.For(admin, secondFactor: false, extraClaims: new Dictionary<string, object> { ["second_factor_verified"] = "true" });

        var response = await _host.CreateClientWith(token).GetAsync("/v1/system/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_who_is_not_a_system_admin_is_forbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _host.CreateClient(owner, secondFactor: true).GetAsync("/v1/system/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        var response = await _host.CreateClient().GetAsync("/v1/system/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
