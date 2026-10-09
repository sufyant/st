using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Tenancy;

namespace Api.IntegrationTests;

// GET /v1/me/tenants runs without a tenant (T4): the caller's active tenants, read through the policy on their own memberships.
public sealed class MyTenantsTests(Database database) : IAsyncLifetime
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
    public async Task ListMyTenants_MemberOfTwoTenants_ReturnsThoseTwoByName()
    {
        var beta = await _catalog.AddTenantAsync(name: "Beta Ltd");
        var alpha = await _catalog.AddTenantAsync(name: "Alpha Ltd");
        var user = await _catalog.AddMemberAsync(beta.Id);
        await _catalog.AddMemberAsync(alpha.Id, user, role: "Owner");
        var notMine = await _catalog.AddTenantAsync(name: "Aardvark Ltd");
        await _catalog.AddMemberAsync(notMine.Id);

        var response = await _api.CreateClient(user).GetAsync("/v1/me/tenants", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Tenants(page).ShouldBe([(alpha.Id, "Alpha Ltd", alpha.Slug), (beta.Id, "Beta Ltd", beta.Slug)]);
        page.GetProperty("items")[0].EnumerateObject().Select(field => field.Name).ShouldBe(["id", "name", "slug"], ignoreOrder: true);
        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(50);
        page.GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task ListMyTenants_TwoTenantsOfTheSameName_AreOrderedById()
    {
        var random = Guid.NewGuid().ToString();
        var later = await _catalog.AddTenantAsync(name: "Same Ltd", tenantId: Guid.Parse($"f{random[1..]}"));
        var earlier = await _catalog.AddTenantAsync(name: "Same Ltd", tenantId: Guid.Parse($"0{random[1..]}"));
        var user = await _catalog.AddMemberAsync(later.Id);
        await _catalog.AddMemberAsync(earlier.Id, user);

        var page = await _api.CreateClient(user).GetFromJsonAsync<JsonElement>("/v1/me/tenants", Cancellation);

        Tenants(page).ShouldBe([(earlier.Id, "Same Ltd", earlier.Slug), (later.Id, "Same Ltd", later.Slug)]);
    }

    [Fact]
    public async Task ListMyTenants_ATenantThatIsNotActive_IsNotListed()
    {
        var active = await _catalog.AddTenantAsync(name: "Active Ltd");
        var provisioning = await _catalog.AddTenantAsync(status: "Provisioning", name: "Provisioning Ltd");
        var user = await _catalog.AddMemberAsync(active.Id);
        await _catalog.AddMemberAsync(provisioning.Id, user);

        var page = await _api.CreateClient(user).GetFromJsonAsync<JsonElement>("/v1/me/tenants", Cancellation);

        Tenants(page).ShouldBe([(active.Id, "Active Ltd", active.Slug)]);
        page.GetProperty("total").GetInt32().ShouldBe(1);
    }

    // Someone signed in with the identity provider who has never joined a tenant has no catalog user yet.
    [Fact]
    public async Task ListMyTenants_WithoutACatalogUser_ReturnsAnEmptyList()
    {
        var response = await _api.CreateClient($"user_{Guid.NewGuid():N}").GetAsync("/v1/me/tenants", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Tenants(page).ShouldBeEmpty();
        page.GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task ListMyTenants_WithoutAToken_IsUnauthorized()
    {
        var response = await _api.CreateClient().GetAsync("/v1/me/tenants", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListMyTenants_ALaterPage_ReturnsTheRestAndTheTotal()
    {
        var first = await _catalog.AddTenantAsync(name: "First Ltd");
        var second = await _catalog.AddTenantAsync(name: "Second Ltd");
        var user = await _catalog.AddMemberAsync(first.Id);
        await _catalog.AddMemberAsync(second.Id, user);

        var page = await _api.CreateClient(user).GetFromJsonAsync<JsonElement>("/v1/me/tenants?page=2&pageSize=1", Cancellation);

        Tenants(page).ShouldBe([(second.Id, "Second Ltd", second.Slug)]);
        page.GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("pageSize=101", "paging.page_size_invalid")]
    [InlineData("page=0", "paging.page_invalid")]
    public async Task ListMyTenants_PagingOutsideItsRange_IsRejected(string query, string code)
    {
        var user = await _catalog.AddUserAsync();

        var response = await _api.CreateClient(user).GetAsync($"/v1/me/tenants?{query}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe(code);
    }

    private static List<(Guid Id, string Name, string Slug)> Tenants(JsonElement page) =>
    [
        .. page.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("name").GetString()!, item.GetProperty("slug").GetString()!)),
    ];
}
