using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tenancy;

namespace Api.IntegrationTests;

// What system admins do through the admin API (0031).
public sealed class SystemAdminEndpointTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application),
            reportingConnectionString: database.ConnectionStringFor(DatabaseRoles.Reporting));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_system_admin_lists_the_tenants_a_page_at_a_time()
    {
        await _catalog.AddTenantAsync();
        await _catalog.AddTenantAsync();
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

        var page = await admin.GetFromJsonAsync<JsonElement>("/v1/admin/tenants?page=2&pageSize=1", Cancellation);

        page.GetProperty("items").GetArrayLength().ShouldBe(1);
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32()).ShouldBe((2, 1));
        page.GetProperty("totalCount").GetInt64().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_tenant_list_is_paginated_by_default()
    {
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

        var page = await admin.GetFromJsonAsync<JsonElement>("/v1/admin/tenants", Cancellation);

        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32()).ShouldBe((1, 50));
    }

    [Fact]
    public async Task A_page_larger_than_the_limit_is_refused()
    {
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

        var response = await admin.GetAsync("/v1/admin/tenants?pageSize=101", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_list_the_tenants()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(owner, secondFactor: true).GetAsync("/v1/admin/tenants", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // A tenant's first owner is invited by a system admin, who is not a member and grants a role they do not hold themselves.
    [Fact]
    public async Task A_system_admin_invites_the_first_owner_of_a_tenant()
    {
        var tenant = await _catalog.AddTenantAsync(status: "Provisioning");
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);
        var owner = $"user_{Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid():N}@example.com";
        _api.Identity.AddAccount(owner, email);
        var ownerRole = await database.ScalarAsync<Guid>("SELECT id FROM catalog.roles WHERE built_in = 'Owner'");

        var invited = await admin.PostAsJsonAsync($"/v1/admin/tenants/{tenant.Slug}/invitations", new { email, roleId = ownerRole }, Cancellation);
        await _api.CreateClient(owner).PostAsJsonAsync("/v1/invitations/accept", new { token = _api.Sender.TokenSentTo(email) }, Cancellation);

        invited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.ScalarAsync<long>(
            $"""
            SELECT count(*) FROM catalog.memberships JOIN catalog.users ON users.id = memberships.user_id
            WHERE memberships.tenant_id = '{tenant.Id}' AND users.external_id = '{owner}' AND memberships.role_id = '{ownerRole}'
            """)).ShouldBe(1);
    }
}
