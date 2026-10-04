using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Api.IntegrationTests;

// The admin API is a separate route group with its own authorization, and needs a second factor (0031).
public sealed class SystemAdminRouteTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = await PipelineHost.StartAsync(configureServices: services => services.AddFakeLogging());

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_system_admin_with_a_second_factor_reaches_the_admin_routes()
    {
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin, secondFactor: true).GetAsync("/v1/admin/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_system_admin_without_a_second_factor_is_forbidden()
    {
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin).GetAsync("/v1/admin/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // Only the host's reading of fva may vouch for a second factor; a claim of that name inside the token proves nothing.
    [Fact]
    public async Task A_second_factor_claimed_by_the_token_itself_is_not_trusted()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var token = TestTokens.For(admin, secondFactor: false, extraClaims: new Dictionary<string, object> { ["second_factor_verified"] = "true" });

        var response = await _host.CreateClientWith(token).GetAsync("/v1/admin/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_who_is_not_a_system_admin_is_forbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _host.CreateClient(owner, secondFactor: true).GetAsync("/v1/admin/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_who_is_not_a_system_admin_cannot_enter_a_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _host.CreateClient(owner, secondFactor: true).GetAsync($"/v1/admin/tenants/{tenant.Slug}/probes", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        var response = await _host.CreateClient().GetAsync("/v1/admin/ping", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Admins see tenant data only the way the tenant itself would: row level security applies to them as usual.
    [Fact]
    public async Task A_system_admin_inside_a_tenant_sees_only_that_tenants_data()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        await WriteProbeAsync(tenant.Id, "theirs");
        await WriteProbeAsync(other.Id, "someone else's");
        var admin = _host.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

        var probes = await admin.GetFromJsonAsync<string[]>($"/v1/admin/tenants/{tenant.Slug}/probes", Cancellation);

        probes.ShouldBe(["theirs"]);
    }

    [Fact]
    public async Task A_system_admin_inside_a_tenant_runs_under_that_tenant_in_the_database()
    {
        var tenant = await _catalog.AddTenantAsync(status: "Provisioning");
        var admin = _host.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

        var setting = await admin.GetStringAsync($"/v1/admin/tenants/{tenant.Slug}/tenant-setting", Cancellation);

        setting.ShouldBe(tenant.Id.ToString());
    }

    [Fact]
    public async Task A_system_admin_entering_a_tenant_is_recorded_as_a_security_event()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddSystemAdminAsync();

        await _host.CreateClient(admin, secondFactor: true).GetAsync($"/v1/admin/tenants/{tenant.Slug}/probes", Cancellation);

        var entry = SecurityEvents().Where(record => record.Message.Contains(tenant.Id.ToString(), StringComparison.Ordinal)).ShouldHaveSingleItem();
        entry.Message.ShouldContain(admin);
    }

    [Fact]
    public async Task A_system_admin_does_not_find_an_unknown_tenant()
    {
        var admin = await _catalog.AddSystemAdminAsync();

        var response = await _host.CreateClient(admin, secondFactor: true).GetAsync("/v1/admin/tenants/no-such-tenant/probes", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Without a second factor an admin learns nothing about tenants, not even which slugs exist.
    [Fact]
    public async Task A_system_admin_without_a_second_factor_cannot_tell_whether_a_tenant_exists()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = _host.CreateClient(await _catalog.AddSystemAdminAsync());

        var known = await admin.GetAsync($"/v1/admin/tenants/{tenant.Slug}/probes", Cancellation);
        var unknown = await admin.GetAsync("/v1/admin/tenants/no-such-tenant/probes", Cancellation);

        (known.StatusCode, unknown.StatusCode).ShouldBe((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
    }

    private async Task WriteProbeAsync(Guid tenantId, string value)
    {
        var member = await _catalog.AddMemberAsync(tenantId);
        var slug = await database.ScalarAsync<string>($"SELECT slug FROM catalog.tenants WHERE id = '{tenantId}'");
        var response = await _host.CreateClient(member).PostAsJsonAsync($"/v1/tenants/{slug}/probes", new { value }, Cancellation);
        response.EnsureSuccessStatusCode();
    }

    private List<FakeLogRecord> SecurityEvents() =>
        [.. _host.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Category == "Api.Security")];
}
