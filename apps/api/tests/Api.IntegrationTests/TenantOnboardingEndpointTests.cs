using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application.Tenants;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Tenancy;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// A system admin creates a tenant through the admin API, and the onboarding saga runs through the outbox (0026, 0031).
public sealed class TenantOnboardingEndpointTests(Database database) : IAsyncLifetime
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
    public async Task A_new_tenant_answers_as_provisioning_and_becomes_active_with_its_first_owner_invited()
    {
        var admin = await AdminAsync();
        var slug = Slug();
        var owner = $"{Guid.NewGuid():N}@example.com";

        var created = await _api.WaitingForMessagesAsync(() => admin.PostAsJsonAsync("/v1/admin/tenants", new { slug, ownerEmail = owner }, Cancellation));

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("status").GetString().ShouldBe("Provisioning");
        (await StatusOfAsync(slug)).ShouldBe("Active");
        _api.Sender.Sent.ShouldContain(sent => sent.Email == owner);
    }

    [Fact]
    public async Task A_slug_another_tenant_has_is_a_conflict()
    {
        var admin = await AdminAsync();
        var existing = await _catalog.AddTenantAsync();

        var created = await admin.PostAsJsonAsync("/v1/admin/tenants", new { slug = existing.Slug, ownerEmail = "owner@example.com" }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await created.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("tenant.slug_taken");
    }

    [Theory]
    [InlineData("Not A Slug", "owner@example.com")]
    [InlineData("valid-slug", "not an email")]
    public async Task A_tenant_needs_a_url_safe_slug_and_an_owner_email(string slug, string ownerEmail)
    {
        var admin = await AdminAsync();

        var created = await admin.PostAsJsonAsync("/v1/admin/tenants", new { slug, ownerEmail }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_create_a_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var created = await _api.CreateClient(owner, secondFactor: true)
            .PostAsJsonAsync("/v1/admin/tenants", new { slug = Slug(), ownerEmail = "owner@example.com" }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // The database refuses to activate this one tenant, as a real failure would; the step is retried, then compensated (0026).
    [Fact]
    public async Task When_a_step_keeps_failing_the_tenant_is_left_failed_and_no_one_is_invited()
    {
        var admin = await AdminAsync();
        var slug = Slug();
        var owner = $"{Guid.NewGuid():N}@example.com";
        await RefuseActivationOfAsync(slug);

        var messages = await _api.TrackMessagesAsync(() =>
            admin.PostAsJsonAsync("/v1/admin/tenants", new { slug, ownerEmail = owner }, Cancellation));

        (await StatusOfAsync(slug)).ShouldBe("Failed");
        messages.MovedToErrorQueue.SingleMessage<ActivateTenant>().ShouldNotBeNull();
        _api.Sender.Sent.ShouldNotContain(sent => sent.Email == owner);
    }

    private async Task<HttpClient> AdminAsync() => _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

    private static string Slug() => $"tenant-{Guid.NewGuid():N}"[..20];

    private Task<string?> StatusOfAsync(string slug) => database.ScalarAsync<string>($"SELECT status FROM catalog.tenants WHERE slug = '{slug}'");

    private async Task RefuseActivationOfAsync(string slug)
    {
        var name = $"refuse_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(database.SuperuserConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE FUNCTION catalog.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'activation refused'; END $$;
            CREATE TRIGGER {name} BEFORE UPDATE ON catalog.tenants
                FOR EACH ROW WHEN (NEW.slug = '{slug}' AND NEW.status = 'Active') EXECUTE FUNCTION catalog.{name}();
            """,
            connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
