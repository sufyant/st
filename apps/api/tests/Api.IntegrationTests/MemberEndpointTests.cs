using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Tenancy;

namespace Api.IntegrationTests;

// Roles are assigned to memberships, and a tenant always keeps at least one owner (0030).
public sealed class MemberEndpointTests(Database database) : IAsyncLifetime
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
    public async Task An_owner_makes_a_member_an_owner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await ChangeRoleAsync(tenant.Slug, owner, member, "Owner");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RoleOfAsync(tenant.Id, member)).ShouldBe("Owner");
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await ChangeRoleAsync(tenant.Slug, owner, owner, "Admin");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).ShouldBe("membership.last_owner");
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(owner).DeleteAsync($"/v1/tenants/{tenant.Slug}/members/{await UserIdOfAsync(owner)}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).ShouldBe("membership.last_owner");
    }

    [Fact]
    public async Task An_admin_cannot_demote_an_owner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");

        var response = await ChangeRoleAsync(tenant.Slug, admin, owner, "Viewer");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RoleOfAsync(tenant.Id, owner)).ShouldBe("Owner");
    }

    [Fact]
    public async Task An_admin_cannot_make_anyone_an_owner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await ChangeRoleAsync(tenant.Slug, admin, member, "Owner");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_without_the_permission_to_manage_members_is_forbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        var other = await _catalog.AddMemberAsync(tenant.Id);

        var response = await ChangeRoleAsync(tenant.Slug, member, other, "Viewer");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Object level: a member of another tenant is not found through this tenant's routes.
    [Fact]
    public async Task A_member_of_another_tenant_cannot_be_changed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var theirs = await _catalog.AddMemberAsync(other.Id);

        var response = await ChangeRoleAsync(tenant.Slug, owner, theirs, "Viewer");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RoleOfAsync(other.Id, theirs)).ShouldBe("Member");
    }

    [Fact]
    public async Task A_removed_member_no_longer_finds_the_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        await _api.CreateClient(owner).DeleteAsync($"/v1/tenants/{tenant.Slug}/members/{await UserIdOfAsync(member)}", Cancellation);

        var response = await ChangeRoleAsync(tenant.Slug, member, owner, "Owner");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> ChangeRoleAsync(string slug, string actor, string member, string builtInRole)
    {
        var roleId = await database.ScalarAsync<Guid>($"SELECT id FROM catalog.roles WHERE built_in = '{builtInRole}'");
        return await _api.CreateClient(actor).PutAsJsonAsync(
            $"/v1/tenants/{slug}/members/{await UserIdOfAsync(member)}/role", new { roleId }, Cancellation);
    }

    private Task<Guid> UserIdOfAsync(string externalId) =>
        database.ScalarAsync<Guid>($"SELECT id FROM catalog.users WHERE external_id = '{externalId}'");

    private Task<string?> RoleOfAsync(Guid tenantId, string externalId) =>
        database.ScalarAsync<string>(
            $"""
            SELECT roles.name FROM catalog.memberships
            JOIN catalog.users ON users.id = memberships.user_id JOIN catalog.roles ON roles.id = memberships.role_id
            WHERE memberships.tenant_id = '{tenantId}' AND users.external_id = '{externalId}'
            """);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();
}
