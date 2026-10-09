using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Notifications.Contracts;
using Tenancy;

namespace Api.IntegrationTests;

// Section 1: one build output runs as two process types, chosen by Host:Role. The web host serves the API and runs the command a
// request sends; whatever that command sends on is handled by a worker host. The two hosts share a database of their own here, so no
// other test's host takes part.
public sealed class HostRoleTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // The messages of every onboarding step after the first, as section 6 lists them.
    private static readonly Type[] StepsAfterTheFirst =
    [
        typeof(RegisterOwnerWithIdentityProvider),
        typeof(OwnerRegistered),
        typeof(ActivateTenant),
        typeof(TenantActivated),
        typeof(OwnerInvitationReady),
        typeof(InvitationEmailSent),
    ];

    [Fact]
    public async Task OnboardTenant_OnAWebAndAWorkerHost_RunsEveryStepAfterTheFirstInTheWorker()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        var runs = new HandlerRuns();
        await using var web = Host(name, "web", runs);
        await using var worker = Host(name, "worker", runs);
        _ = worker.Services;
        var admin = web.CreateClient(await AddSystemAdminAsync(name), secondFactor: true);

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = "acme", ownerEmail = "owner@acme.test" });
        await runs.HandledAsync<InvitationEmailSent>();

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        runs.In("web").ShouldBe([typeof(StartTenantOnboarding)]);
        runs.In("worker").Distinct().ShouldBe(StepsAfterTheFirst, ignoreOrder: true);
        (await OnboardingStateAsync(name, await IdOfAsync(created))).ShouldBe("Completed");
        worker.Email.Sent.ShouldHaveSingleItem().To.ShouldBe("owner@acme.test");
    }

    [Fact]
    public async Task OnboardTenant_OnAWebHostAlone_WaitsUntilAWorkerStarts()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        var runs = new HandlerRuns();
        await using var web = Host(name, "web", runs);
        var admin = web.CreateClient(await AddSystemAdminAsync(name), secondFactor: true);
        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = "acme", ownerEmail = "owner@acme.test" });
        var tenantId = await IdOfAsync(created);
        var waiting = await OnboardingStateAsync(name, tenantId);
        var queued = await database.ScalarAsync<long>(
            """
            SELECT count(*) FROM (
                SELECT message_type FROM wolverine.wolverine_queue_messages
                UNION ALL SELECT message_type FROM wolverine.wolverine_outgoing_envelopes) AS waiting
            WHERE message_type LIKE '%RegisterOwnerWithIdentityProvider'
            """,
            database: name);

        await using var worker = Host(name, "worker", runs);
        _ = worker.Services;
        await runs.HandledAsync<InvitationEmailSent>();

        waiting.ShouldBe("Registering");
        queued.ShouldBe(1L);
        runs.In("web").ShouldBe([typeof(StartTenantOnboarding)]);
        (await OnboardingStateAsync(name, tenantId)).ShouldBe("Completed");
    }

    // A worker serves no API: only the health endpoints answer.
    [Theory]
    [InlineData("/v1/me/tenants", HttpStatusCode.NotFound)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/health/ready", HttpStatusCode.OK)]
    public async Task CallTheApi_OnAWorkerHost_AnswersOnlyTheHealthEndpoints(string path, HttpStatusCode expected)
    {
        await using var worker = Host(await database.CreateMigratedDatabaseAsync(), "worker", new HandlerRuns());

        var response = await worker.CreateClient("user_worker").GetAsync(path, Cancellation);

        response.StatusCode.ShouldBe(expected);
    }

    private ApiFactory Host(string databaseName, string role, HandlerRuns runs) => new(
        database.ConnectionStringFor(DatabaseRoles.Application, databaseName),
        settings: new Dictionary<string, string?> { ["Host:Role"] = role },
        configureServices: HandlerRunsOfHost.Register(role, runs));

    private async Task<string> AddSystemAdminAsync(string databaseName)
    {
        var externalId = $"user_{Guid.NewGuid():N}";
        await database.ScalarAsync<object>(
            $"""
            INSERT INTO catalog.users (id, external_id) VALUES ('{Guid.NewGuid()}', '{externalId}');
            INSERT INTO catalog.system_admins (user_id, granted_at) SELECT id, now() FROM catalog.users WHERE external_id = '{externalId}';
            """,
            database: databaseName);

        return externalId;
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage created) =>
        (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

    private Task<string?> OnboardingStateAsync(string databaseName, Guid tenantId) =>
        database.ScalarAsSuperuserAsync<string>($"SELECT state FROM catalog.tenant_onboardings WHERE id = '{tenantId}'", databaseName);
}
