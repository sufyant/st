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
    public async Task A_member_whose_role_lacks_the_permission_is_forbidden_with_problem_details()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await PostGuardedAsync(tenant.Slug, owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // A tenant the user cannot enter looks the same as one that does not exist, also behind a permission.
    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_find_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();

        var response = await PostGuardedAsync(tenant.Slug, outsider);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // System admins enter tenants through the admin routes only.
    [Fact]
    public async Task A_system_admin_who_is_not_a_member_does_not_find_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin, secondFactor: true)
            .PostAsync($"/v1/tenants/{tenant.Slug}/guarded", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> PostGuardedAsync(string slug, string userId) =>
        _host.CreateClient(userId).PostAsync($"/v1/tenants/{slug}/guarded", null, TestContext.Current.CancellationToken);
}
