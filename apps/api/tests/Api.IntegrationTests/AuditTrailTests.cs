using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using SharedKernel;
using Tenancy;

namespace Api.IntegrationTests;

// The audit log records successful state-changing commands, denied authorization attempts inside a tenant and system admin entries
// into a tenant (0040). Each record travels through the outbox and is stored by the Audit module under its tenant.
public sealed class AuditTrailTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ApplicationConnectionString, configureServices: services => services.AddFakeLogging());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_successful_command_is_recorded_with_its_actor_and_the_record_it_created()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var response = await _api.WaitingForMessagesAsync(() => _api.CreateClient(owner).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = new[] { Permissions.MembersInvite } }, Cancellation));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roleId = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
        var entry = (await EntriesAsync(tenant.Id)).ShouldHaveSingleItem();
        (entry.Kind, entry.Operation, entry.ActorId).ShouldBe(("Command", "CreateRole", owner));
        var details = JsonDocument.Parse(entry.Details).RootElement;
        details.GetProperty("command").GetProperty("name").GetString().ShouldBe("Recruiter");
        details.GetProperty("result").GetProperty("id").GetGuid().ShouldBe(roleId);
    }

    // The record is written in the command's transaction, so a command whose work is undone leaves none.
    [Fact]
    public async Task A_command_that_fails_is_not_recorded()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");

        var response = await _api.WaitingForMessagesAsync(() => _api.CreateClient(admin).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Co-owner", permissions = new[] { Permissions.OwnersManage } }, Cancellation));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await EntriesAsync(tenant.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_denied_a_permission_is_recorded()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _api.WaitingForMessagesAsync(() => _api.CreateClient(member).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = Array.Empty<string>() }, Cancellation));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var entry = (await EntriesAsync(tenant.Id)).ShouldHaveSingleItem();
        (entry.Kind, entry.Operation, entry.ActorId).ShouldBe(("Denied", $"POST /v1/tenants/{tenant.Slug}/roles", member));
    }

    // The audit log is tenant-scoped; a caller who never entered the tenant leaves a security event in the log instead.
    [Fact]
    public async Task A_denial_outside_any_resolved_tenant_goes_to_the_security_log()
    {
        var tenant = await _catalog.AddTenantAsync();
        var stranger = await _catalog.AddUserAsync();

        var response = await _api.WaitingForMessagesAsync(() => _api.CreateClient(stranger).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/roles", new { name = "Recruiter", permissions = Array.Empty<string>() }, Cancellation));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EntriesAsync(tenant.Id)).ShouldBeEmpty();
        _api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record =>
            record.Category == "Api.Security" && record.Id.Name == "AuthorizationDenied" && record.Message.Contains(stranger, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_system_admin_entering_a_tenant_is_recorded()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddSystemAdminAsync();
        var roleId = await database.ScalarAsync<Guid>("SELECT id FROM catalog.roles WHERE built_in = 'Member'");

        var response = await _api.WaitingForMessagesAsync(() => _api.CreateClient(admin, secondFactor: true).PostAsJsonAsync(
            $"/v1/admin/tenants/{tenant.Slug}/invitations", new { email = $"{Guid.NewGuid():N}@example.com", roleId }, Cancellation));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entries = await EntriesAsync(tenant.Id);
        entries.ShouldContain(entry => entry.Kind == "SystemAdminEntry"
            && entry.Operation == $"POST /v1/admin/tenants/{tenant.Slug}/invitations"
            && entry.ActorId == admin);
        entries.ShouldContain(entry => entry.Kind == "Command" && entry.Operation == "CreateInvitationAsSystemAdmin" && entry.ActorId == admin);
    }

    // The owner reads every tenant's rows, so the test sees what the Audit module stored for this tenant.
    private async Task<List<(string Kind, string Operation, string ActorId, string Details)>> EntriesAsync(Guid tenantId)
    {
        var rows = await database.ScalarAsync<string>(
            $"""
            SELECT coalesce(json_agg(json_build_array(kind, operation, actor_id, details::text) ORDER BY occurred_at), '[]')::text
            FROM audit.entries WHERE tenant_id = '{tenantId}'
            """,
            DatabaseRoles.Owner);

        return [.. JsonDocument.Parse(rows!).RootElement.EnumerateArray().Select(row =>
            (row[0].GetString()!, row[1].GetString()!, row[2].GetString()!, row[3].GetString() ?? "null"))];
    }
}
