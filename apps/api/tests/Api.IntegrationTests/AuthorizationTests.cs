using System.Net;
using SharedKernel;

namespace Api.IntegrationTests;

// Function level authorization: code checks permissions, never roles (0030).
public sealed class AuthorizationTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_member_whose_role_holds_the_permission_is_let_through()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await PostGuardedAsync(tenant.Slug, owner);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_whose_role_lacks_the_permission_is_forbidden_with_problem_details()
    {
        var tenant = await _catalog.AddTenantAsync();
        var viewer = await _catalog.AddMemberAsync(tenant.Id, role: "Viewer");

        var response = await PostGuardedAsync(tenant.Slug, viewer);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task A_custom_role_grants_its_permissions_without_code_changes()
    {
        var tenant = await _catalog.AddTenantAsync();
        var recruiter = await _catalog.AddCustomRoleAsync(tenant.Id, Permissions.MembersInvite);
        var member = await _catalog.AddMemberWithRoleAsync(tenant.Id, recruiter);

        var response = await PostGuardedAsync(tenant.Slug, member);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Permissions are held in a tenant: the same person is an owner in one tenant and a viewer in another.
    [Fact]
    public async Task A_permission_held_in_one_tenant_does_not_reach_another()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var user = await _catalog.AddMemberAsync(other.Id, role: "Owner");
        await _catalog.AddMemberAsync(tenant.Id, user, role: "Viewer");

        var response = await PostGuardedAsync(tenant.Slug, user);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // A tenant the user cannot enter looks the same as one that does not exist, also behind a permission (0015).
    [Fact]
    public async Task A_user_who_is_not_a_member_does_not_find_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();

        var response = await PostGuardedAsync(tenant.Slug, outsider);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // System admins enter tenants through the admin routes only (0031).
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
