using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Tenancy;

namespace Api.IntegrationTests;

// GET /v1/tenants/{tenantId}/members, the read endpoint behind the tenant door: the door, membership, row level security and the
// permission check, through the composed application.
public sealed class MemberListTests(Database database) : IAsyncLifetime
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
    public async Task ListMembers_AsAMember_ReturnsTheMembersOfThatTenantOnly()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Member");
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        await _catalog.AddMemberAsync(other.Id, role: "Owner");

        var response = await _api.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Id}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Members(page).ShouldBe(
        [
            (await _catalog.UserIdOfAsync(owner), "Owner"),
            (await _catalog.UserIdOfAsync(admin), "Admin"),
            (await _catalog.UserIdOfAsync(member), "Member"),
        ]);
        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(50);
        page.GetProperty("total").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task ListMembers_OfTheSameRole_AreOrderedByUserId()
    {
        var tenant = await _catalog.AddTenantAsync();
        var random = Guid.NewGuid().ToString();
        var (lower, higher) = (Guid.Parse($"0{random[1..]}"), Guid.Parse($"f{random[1..]}"));
        var later = await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(higher));
        await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(lower));

        var page = await _api.CreateClient(later).GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Id}/members", Cancellation);

        Members(page).ShouldBe([(lower, "Member"), (higher, "Member")]);
    }

    // T2: the tenant comes from the path, whatever the query string or a header says.
    [Fact]
    public async Task ListMembers_AnotherTenantInTheQueryAndAHeader_ReturnsTheTenantOfThePath()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var caller = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        await _catalog.AddMemberAsync(other.Id, caller, role: "Member");
        await _catalog.AddMemberAsync(other.Id, role: "Owner");
        var client = _api.CreateClient(caller);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", other.Id.ToString());

        var page = await client.GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Id}/members?tenantId={other.Id}", Cancellation);

        Members(page).ShouldBe([(await _catalog.UserIdOfAsync(caller), "Owner")]);
    }

    // T3: a tenant the caller cannot enter looks the same as one that does not exist.
    [Fact]
    public async Task ListMembers_AsAUserWhoIsNotAMember_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var outsider = await _catalog.AddUserAsync();

        var response = await _api.CreateClient(outsider).GetAsync($"/v1/tenants/{tenant.Id}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMembers_UnknownTenantId_IsNotFound()
    {
        var user = await _catalog.AddUserAsync();

        var response = await _api.CreateClient(user).GetAsync($"/v1/tenants/{Guid.NewGuid()}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMembers_TenantIdThatIsNotAGuid_IsNotFound()
    {
        var user = await _catalog.AddUserAsync();

        var response = await _api.CreateClient(user).GetAsync("/v1/tenants/not-a-guid/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMembers_TenantThatIsNotActive_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync(status: "Provisioning");
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Id}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // T5: a system admin is not a member, so the tenant door does not let them in.
    [Fact]
    public async Task ListMembers_AsASystemAdminWhoIsNotAMember_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _api.CreateClient(admin, secondFactor: true).GetAsync($"/v1/tenants/{tenant.Id}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // T1: the slug is never part of a path.
    [Fact]
    public async Task ListMembers_TheSlugInThePath_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Slug}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMembers_WithoutAToken_IsUnauthorized()
    {
        var tenant = await _catalog.AddTenantAsync();

        var response = await _api.CreateClient().GetAsync($"/v1/tenants/{tenant.Id}/members", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListMembers_TheMaximumPageSize_IsAccepted()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var page = await _api.CreateClient(member).GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Id}/members?pageSize=100", Cancellation);

        page.GetProperty("pageSize").GetInt32().ShouldBe(100);
    }

    [Fact]
    public async Task ListMembers_ALaterPage_ReturnsTheRestAndTheTotal()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Member");

        var page = await _api.CreateClient(owner).GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Id}/members?page=2&pageSize=2", Cancellation);

        Members(page).ShouldBe([(await _catalog.UserIdOfAsync(member), "Member")]);
        page.GetProperty("page").GetInt32().ShouldBe(2);
        page.GetProperty("pageSize").GetInt32().ShouldBe(2);
        page.GetProperty("total").GetInt32().ShouldBe(3);
    }

    [Theory]
    [InlineData("pageSize=101", "paging.page_size_invalid")]
    [InlineData("pageSize=0", "paging.page_size_invalid")]
    [InlineData("page=0", "paging.page_invalid")]
    public async Task ListMembers_PagingOutsideItsRange_IsRejected(string query, string code)
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _api.CreateClient(member).GetAsync($"/v1/tenants/{tenant.Id}/members?{query}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe(code);
    }

    private static List<(Guid UserId, string Role)> Members(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("userId").GetGuid(), item.GetProperty("role").GetString()!))];
}
