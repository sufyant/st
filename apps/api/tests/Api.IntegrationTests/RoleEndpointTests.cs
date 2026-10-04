using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Tenancy;

namespace Api.IntegrationTests;

// Tenants make their own roles from the permission catalogue (0030).
public sealed class RoleEndpointTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task An_owner_creates_a_custom_role()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(owner).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = new[] { Permissions.MembersInvite } }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var role = await response.Content.ReadFromJsonAsync<RoleBody>(Cancellation);
        role.ShouldNotBeNull();
        (role.Name, role.BuiltIn).ShouldBe(("Recruiter", false));
        role.Permissions.ShouldBe([Permissions.MembersInvite]);
    }

    // Tenant custom roles can never hold system permissions (0031).
    [Fact]
    public async Task A_custom_role_cannot_hold_a_system_permission()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(owner).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Escalated", permissions = new[] { Permissions.SystemTenantsEnter } }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).ShouldBe("role.permission_not_allowed");
    }

    [Fact]
    public async Task A_custom_role_cannot_be_changed_to_hold_a_system_permission()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var role = await _catalog.AddCustomRoleAsync(tenant.Id, Permissions.MembersInvite);

        var response = await _api.CreateClient(owner).PutAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles/{role}", new { name = "Escalated", permissions = new[] { Permissions.SystemMembersInvite } }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).ShouldBe("role.permission_not_allowed");
    }

    [Fact]
    public async Task A_member_without_the_permission_to_manage_roles_is_forbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var viewer = await _catalog.AddMemberAsync(tenant.Id, role: "Viewer");

        var response = await _api.CreateClient(viewer).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = Array.Empty<string>() }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_cannot_create_a_role_that_holds_more_than_they_do()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");

        var response = await _api.CreateClient(admin).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Co-owner", permissions = new[] { Permissions.OwnersManage } }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(response)).ShouldBe("role.beyond_your_permissions");
    }

    // Property level: a client cannot choose the tenant of a row by sending it (mass assignment).
    [Fact]
    public async Task A_role_is_created_in_the_tenant_of_the_request_whatever_the_body_says()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(owner).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = Array.Empty<string>(), tenantId = other.Id }, Cancellation);

        var role = await response.Content.ReadFromJsonAsync<RoleBody>(Cancellation);
        (await database.ScalarAsync<Guid>($"SELECT tenant_id FROM catalog.roles WHERE id = '{role!.Id}'")).ShouldBe(tenant.Id);
    }

    // Object level: rows of another tenant are not found through this tenant's routes.
    [Fact]
    public async Task A_role_of_another_tenant_cannot_be_changed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var theirs = await _catalog.AddCustomRoleAsync(other.Id, Permissions.MembersInvite);

        var response = await _api.CreateClient(owner).PutAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles/{theirs}", new { name = "Taken over", permissions = Array.Empty<string>() }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await database.ScalarAsync<string>($"SELECT name FROM catalog.roles WHERE id = '{theirs}'")).ShouldBe($"Role {theirs:N}");
    }

    [Fact]
    public async Task A_role_of_another_tenant_cannot_be_deleted()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var theirs = await _catalog.AddCustomRoleAsync(other.Id);

        var response = await _api.CreateClient(owner).DeleteAsync($"/v1/tenants/{tenant.Slug}/roles/{theirs}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.roles WHERE id = '{theirs}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_changed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var viewer = await database.ScalarAsync<Guid>("SELECT id FROM catalog.roles WHERE built_in = 'Viewer'");

        var response = await _api.CreateClient(owner).PutAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles/{viewer}", new { name = "Viewer", permissions = new[] { Permissions.MembersInvite } }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).ShouldBe("role.built_in");
    }

    [Fact]
    public async Task A_role_assigned_to_a_member_cannot_be_deleted()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var role = await _catalog.AddCustomRoleAsync(tenant.Id);
        await _catalog.AddMemberWithRoleAsync(tenant.Id, role);

        var response = await _api.CreateClient(owner).DeleteAsync($"/v1/tenants/{tenant.Slug}/roles/{role}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).ShouldBe("role.in_use");
    }

    [Fact]
    public async Task An_unassigned_custom_role_is_deleted()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var role = await _catalog.AddCustomRoleAsync(tenant.Id);

        var response = await _api.CreateClient(owner).DeleteAsync($"/v1/tenants/{tenant.Slug}/roles/{role}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.roles WHERE id = '{role}'")).ShouldBe(0);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();

    private sealed record RoleBody(Guid Id, string Name, bool BuiltIn, string[] Permissions);
}
