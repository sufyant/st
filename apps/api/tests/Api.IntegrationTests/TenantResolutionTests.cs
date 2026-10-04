using System.Net;

namespace Api.IntegrationTests;

public sealed class TenantResolutionTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_member_reaches_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _host.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Slug}/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Tenant routes have a segment of their own, so no slug needs to be reserved for the routes outside a tenant (0033).
    [Fact]
    public async Task A_slug_may_be_the_name_of_a_route_outside_tenants()
    {
        var tenant = await _catalog.AddTenantAsync(slug: "ping");
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _host.CreateClient(member).GetAsync("/v1/tenants/ping/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_without_a_user_is_unauthorized()
    {
        var tenant = await _catalog.AddTenantAsync();

        var response = await _host.CreateClient().GetAsync($"/v1/tenants/{tenant.Slug}/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // A tenant the user cannot enter looks the same as one that does not exist, so its existence is not revealed.
    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_find_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();

        var response = await _host.CreateClient(outsider).GetAsync($"/v1/tenants/{tenant.Slug}/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        var user = await _catalog.AddUserAsync();

        var response = await _host.CreateClient(user).GetAsync("/v1/tenants/no-such-tenant/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task A_member_of_a_tenant_that_is_not_active_does_not_find_it()
    {
        var tenant = await _catalog.AddTenantAsync(status: "Provisioning");
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _host.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Slug}/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
