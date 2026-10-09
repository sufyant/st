using System.Net;

namespace Api.IntegrationTests;

public sealed class RateLimitingTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = await PipelineHost.StartAsync(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1" });

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_tenant_over_its_limit_is_rejected_with_problem_details_and_a_retry_hint()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", member);

        var rejected = await GetAsync($"/v1/tenants/{tenant.Id}/ping", member);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Members_of_a_tenant_share_its_limit()
    {
        var tenant = await _catalog.AddTenantAsync();
        var first = await _catalog.AddMemberAsync(tenant.Id);
        var second = await _catalog.AddMemberAsync(tenant.Id);
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", first);

        var rejected = await GetAsync($"/v1/tenants/{tenant.Id}/ping", second);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_tenant_over_its_limit_does_not_limit_another_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        await _catalog.AddMemberAsync(other.Id, member);
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", member);
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", member);

        var response = await GetAsync($"/v1/tenants/{other.Id}/ping", member);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The slug in the route is never a key: a non-member spends their own limit, not the tenant's.
    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_spend_the_tenant_limit()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        var outsider = await _catalog.AddUserAsync();
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", outsider);

        var response = await GetAsync($"/v1/tenants/{tenant.Id}/ping", member);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_is_limited_as_a_user()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();
        await GetAsync($"/v1/tenants/{tenant.Id}/ping", outsider);

        var rejected = await GetAsync("/v1/ping", outsider);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Outside_a_tenant_each_user_has_their_own_limit()
    {
        await GetAsync("/v1/ping", "user_1");
        await GetAsync("/v1/ping", "user_1");

        var other = await GetAsync("/v1/ping", "user_2");

        other.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Outside_a_tenant_and_without_a_user_each_ip_address_has_its_own_limit()
    {
        await SendFromAsync("203.0.113.1");
        var limited = await SendFromAsync("203.0.113.1");

        var other = await SendFromAsync("203.0.113.2");

        limited.Response.StatusCode.ShouldBe(429);
        other.Response.StatusCode.ShouldBe(200);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string userId) =>
        _host.CreateClient(userId).GetAsync(path, TestContext.Current.CancellationToken);

    private Task<Microsoft.AspNetCore.Http.HttpContext> SendFromAsync(string ipAddress) =>
        _host.Server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Path = "/v1/ping";
            context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        }, TestContext.Current.CancellationToken);
}
