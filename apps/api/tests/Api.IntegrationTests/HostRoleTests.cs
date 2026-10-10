using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Audit.Application;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application;
using Notifications.Contracts;
using Tenancy;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace Api.IntegrationTests;

// Section 1: one build output runs as two process types, chosen by Host:Role. The web host serves the API and runs the command a
// request sends; whatever that command sends on is handled by a worker host. The two hosts share a database of their own here, so no
// other test's host takes part.
public sealed class HostRoleTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // The handlers of every onboarding message after the first, as section 6 lists them: an event with two handlers runs both.
    private static readonly HandlerRun[] HandlersAfterTheFirstStep =
    [
        new(typeof(ActivateTenant), typeof(ActivateTenantHandler)),
        new(typeof(TenantActivated), typeof(TenantOnboarding)),
        new(typeof(TenantActivated), typeof(RecordTenantCreatedHandler)),
        new(typeof(OwnerInvitationReady), typeof(SendOwnerInvitationHandler)),
        new(typeof(InvitationEmailSent), typeof(TenantOnboarding)),
    ];

    private static readonly HandlerRun FirstStep = new(typeof(StartTenantOnboarding), typeof(StartTenantOnboardingHandler));

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
        var tenantId = await IdOfAsync(created);
        await Waiting.UntilAsync(async () => await OnboardingStateAsync(name, tenantId) == "Completed");

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        runs.In("web").ShouldBe([FirstStep]);
        runs.In("worker").Distinct().ShouldBe(HandlersAfterTheFirstStep, ignoreOrder: true);
        (await OnboardingStateAsync(name, tenantId)).ShouldBe("Completed");
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
            WHERE message_type LIKE '%ActivateTenant'
            """,
            database: name);

        await using var worker = Host(name, "worker", runs);
        _ = worker.Services;
        await Waiting.UntilAsync(async () => await OnboardingStateAsync(name, tenantId) == "Completed");

        waiting.ShouldBe("Activating");
        queued.ShouldBe(1L);
        runs.In("web").ShouldBe([FirstStep]);
        (await OnboardingStateAsync(name, tenantId)).ShouldBe("Completed");
    }

    // O3, W4: the Audit handler of the activation runs beside the saga's, in the worker, once.
    [Fact]
    public async Task OnboardTenant_OnAWebAndAWorkerHost_WritesOneTenantCreatedAuditEntry()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        await using var web = Host(name, "web", new HandlerRuns());
        await using var worker = Host(name, "worker", new HandlerRuns());
        _ = worker.Services;
        var admin = web.CreateClient(await AddSystemAdminAsync(name), secondFactor: true);

        var tenantId = await IdOfAsync(await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = "acme", ownerEmail = "owner@acme.test" }));
        await Waiting.UntilAsync(async () => await OnboardingStateAsync(name, tenantId) == "Completed");
        await Waiting.UntilAsync(async () => await TenantCreatedEntriesAsync(name, tenantId) > 0);

        (await OnboardingStateAsync(name, tenantId)).ShouldBe("Completed");
        (await TenantCreatedEntriesAsync(name, tenantId)).ShouldBe(1L);
    }

    // Spike T8 in the production layout (O3, W4): each handler of an event runs in a worker, in its own transaction. The one that
    // throws goes to the dead letter queue; the other commits its row once.
    [Fact]
    public async Task PublishAnEventWithTwoHandlers_OnAWebAndAWorkerHost_OneThrowsTheOtherCommitsOnceInTheWorker()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        await database.CreateProbesAsync(name);
        var runs = new HandlerRuns();
        await using var web = ProbeHost(name, "web", runs);
        await using var worker = ProbeHost(name, "worker", runs);
        _ = worker.Services;
        var tenant = Guid.NewGuid();
        var value = $"ping-{Guid.NewGuid():N}";

        await using (var scope = web.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .PublishAsync(new ProbePinged(value), new DeliveryOptions { TenantId = tenant.ToString() });
        }

        await Waiting.UntilAsync(async () => await DeadLettersOfAsync<ProbePinged>(name) > 0);
        await Waiting.UntilAsync(async () => await ProbesAsync(name, $"recorded:{value}") > 0);

        (await ProbesAsync(name, $"recorded:{value}")).ShouldBe(1L);
        (await ProbesAsync(name, $"failed:{value}")).ShouldBe(0L);
        (await DeadLettersOfAsync<ProbePinged>(name)).ShouldBe(1L);
        runs.In("worker").ShouldBe([new HandlerRun(typeof(ProbePinged), typeof(RecordPingHandler))]);
        runs.In("web").ShouldBeEmpty();
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

    // A host with the probe module's DbContext and the two handlers of ProbePinged.
    private ApiFactory ProbeHost(string databaseName, string role, HandlerRuns runs) => new(
        database.ConnectionStringFor(DatabaseRoles.Application, databaseName),
        settings: new Dictionary<string, string?> { ["Host:Role"] = role },
        configureServices: services =>
        {
            HandlerRunsOfHost.Register(role, runs)(services);
            services.AddDbContextWithWolverineIntegration<ProbeDbContext>(
                (provider, options) => options.UseModuleDatabase(provider, ProbeDbContext.Schema),
                TenancyServiceCollectionExtensions.MessageSchema);
            services.ConfigureWolverine(options =>
            {
                options.Discovery.IncludeType(typeof(RecordPingHandler));
                options.Discovery.IncludeType(typeof(FailOnPingHandler));
            });
        });

    private Task<long> TenantCreatedEntriesAsync(string databaseName, Guid tenantId) =>
        database.ScalarAsSuperuserAsync<long>(
            $"SELECT count(*) FROM audit.entries WHERE tenant_id = '{tenantId}' AND operation = 'tenant.created'", databaseName);

    private Task<long> ProbesAsync(string databaseName, string value) =>
        database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM probes.probes WHERE value = '{value}'", databaseName);

    private Task<long> DeadLettersOfAsync<TMessage>(string databaseName) =>
        database.ScalarAsSuperuserAsync<long>(
            $"SELECT count(*) FROM wolverine.wolverine_dead_letters WHERE message_type = '{typeof(TMessage).FullName}'", databaseName);

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
