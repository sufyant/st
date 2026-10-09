using System.Net;

namespace Api.IntegrationTests;

// Function level authorization: code checks permissions, never roles.
public sealed class AuthorizationTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task CallGuardedEndpoint_RoleLacksThePermission_IsForbiddenWithProblemDetails()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await PostGuardedAsync(tenant.Id, owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // A tenant the user cannot enter looks the same as one that does not exist, also behind a permission.
    [Fact]
    public async Task CallGuardedEndpoint_UserNotAMember_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();

        var response = await PostGuardedAsync(tenant.Id, outsider);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // A system admin is not a member, so the tenant routes do not let them in.
    [Fact]
    public async Task CallGuardedEndpoint_SystemAdminNotAMember_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin, secondFactor: true)
            .PostAsync($"/v1/tenants/{tenant.Id}/guarded", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> PostGuardedAsync(Guid tenantId, string userId) =>
        _host.CreateClient(userId).PostAsync($"/v1/tenants/{tenantId}/guarded", null, TestContext.Current.CancellationToken);
}
