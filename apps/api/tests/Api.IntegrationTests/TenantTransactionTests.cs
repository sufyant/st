using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// Wolverine opens, saves and commits the transaction of every handler, and the transaction declares the tenant of the message
// when it starts (W1, W2). Row level security does the rest.
public sealed class TenantTransactionTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_command_from_a_tenant_request_runs_under_that_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));

        await member.PostAsJsonAsync($"/v1/tenants/{tenant.Id}/probes", new { value = "mine" }, Cancellation);

        var probes = await member.GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Id}/probes", Cancellation);
        probes.ShouldBe(["mine"]);
    }

    [Fact]
    public async Task A_tenant_request_does_not_see_the_data_of_another_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var otherMember = _host.CreateClient(await _catalog.AddMemberAsync(other.Id));
        await otherMember.PostAsJsonAsync($"/v1/tenants/{other.Id}/probes", new { value = "theirs" }, Cancellation);

        var probes = await _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id))
            .GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Id}/probes", Cancellation);

        probes.ShouldBeEmpty();
    }

    // Property level: a client cannot choose the tenant of a row by sending it (mass assignment).
    [Fact]
    public async Task A_row_is_written_in_the_tenant_of_the_request_whatever_the_body_says()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));
        var value = $"probe-{Guid.NewGuid():N}";

        await member.PostAsJsonAsync($"/v1/tenants/{tenant.Id}/probes", new { value, tenantId = other.Id }, Cancellation);

        (await database.ScalarAsSuperuserAsync<Guid>($"SELECT tenant_id FROM probes.probes WHERE value = '{value}'")).ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task A_command_that_fails_leaves_nothing_written()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));

        var response = await member.PostAsJsonAsync($"/v1/tenants/{tenant.Id}/failing-probes", new { value = "lost" }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await member.GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Id}/probes", Cancellation)).ShouldBeEmpty();
    }

    // Spike T1.
    [Fact]
    public async Task InvokeForTenant_HandlerAddsARow_RowIsSavedUnderThatTenant()
    {
        var tenant = Guid.NewGuid();
        var value = $"probe-{Guid.NewGuid():N}";

        await Bus().InvokeForTenantAsync<Result>(tenant.ToString(), new WriteProbe(value), Cancellation);

        (await database.ScalarAsSuperuserAsync<Guid>($"SELECT tenant_id FROM probes.probes WHERE value = '{value}'")).ShouldBe(tenant);
    }

    // Spike T2.
    [Fact]
    public async Task InvokeForTenant_AnotherTenantHasRows_HandlerSeesOnlyItsOwn()
    {
        var (tenant, other) = (Guid.NewGuid(), Guid.NewGuid());
        await Bus().InvokeForTenantAsync<Result>(tenant.ToString(), new WriteProbe("own"), Cancellation);
        await Bus().InvokeForTenantAsync<Result>(other.ToString(), new WriteProbe("theirs"), Cancellation);

        var seen = await Bus().InvokeForTenantAsync<TenantObservation>(tenant.ToString(), new ObserveTenant(), Cancellation);

        seen.ShouldBe(new TenantObservation(tenant.ToString(), VisibleProbes: 1));
    }

    // Spike T3: the cascaded message goes through a durable local queue and comes back with the tenant in its envelope.
    [Fact]
    public async Task CascadeThroughALocalQueue_FromATenantHandler_RunsUnderThatTenant()
    {
        var tenant = Guid.NewGuid();

        await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync(tenant.ToString(), new WriteTenantSettingLater()));

        (await ReadProbesAsync(tenant)).ShouldBe([$"setting:{tenant}"]);
    }

    [Fact]
    public async Task A_cascaded_message_keeps_the_tenant_of_the_message_that_caused_it()
    {
        var tenant = Guid.NewGuid();

        await _host.Host.ExecuteAndWaitAsync(context => context.InvokeForTenantAsync(tenant.ToString(), new WriteProbeLater("cascaded")));

        (await ReadProbesAsync(tenant)).ShouldBe(["cascaded"]);
    }

    [Fact]
    public async Task A_published_message_is_handled_under_its_tenant()
    {
        var tenant = Guid.NewGuid();

        await _host.Host.ExecuteAndWaitAsync(
            context => context.PublishAsync(new WriteProbe("published"), new DeliveryOptions { TenantId = tenant.ToString() }).AsTask());

        (await ReadProbesAsync(tenant)).ShouldBe(["published"]);
    }

    // Spike T5: a pooled connection never carries a tenant to the next transaction that uses it.
    [Fact]
    public async Task InvokeOnePooledConnection_TenantATenantBThenNoTenant_NoTenantLeaksToTheNextUse()
    {
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        await Bus().InvokeForTenantAsync<Result>(tenantA.ToString(), new WriteProbe("a"), Cancellation);
        await using var host = await PipelineHost.StartAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Pooled"] = new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString,
        });
        var bus = host.Host.MessageBus();

        var seenByA = await bus.InvokeForTenantAsync<TenantObservation>(tenantA.ToString(), new ObserveTenant(), Cancellation);
        var seenByB = await bus.InvokeForTenantAsync<TenantObservation>(tenantB.ToString(), new ObserveTenant(), Cancellation);
        var seenWithout = await bus.InvokeAsync<TenantObservation>(new ObserveTenant(), Cancellation);
        await using var afterwards = host.Services.GetRequiredService<NpgsqlDataSource>()
            .CreateCommand("SELECT NULLIF(current_setting('app.tenant_id', true), '')");
        var leftOnTheConnection = await afterwards.ExecuteScalarAsync(Cancellation);

        seenByA.ShouldBe(new TenantObservation(tenantA.ToString(), VisibleProbes: 1));
        seenByB.ShouldBe(new TenantObservation(tenantB.ToString(), VisibleProbes: 0));
        seenWithout.ShouldBe(new TenantObservation(null, VisibleProbes: 0));
        leftOnTheConnection.ShouldBe(DBNull.Value);
    }

    // Spike T6.
    [Fact]
    public async Task InvokeWithoutATenant_TenantRowsExist_HandlerSeesNone()
    {
        await Bus().InvokeForTenantAsync<Result>(Guid.NewGuid().ToString(), new WriteProbe("tenant data"), Cancellation);

        var probes = await Bus().InvokeAsync<Result<string[]>>(new ReadProbes(), Cancellation);

        probes.Value.ShouldBeEmpty();
    }

    // Spike T6: the row's tenant column defaults to the declared tenant, and row level security refuses a row of no tenant.
    [Fact]
    public async Task InvokeWithoutATenant_HandlerAddsARow_InsertIsRefused()
    {
        var value = $"orphan-{Guid.NewGuid():N}";

        var invoke = () => Bus().InvokeAsync<Result>(new WriteProbe(value), Cancellation);

        var failure = await invoke.ShouldThrowAsync<Exception>();
        PostgresErrorOf(failure).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM probes.probes WHERE value = '{value}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_message_with_a_malformed_tenant_is_not_handled()
    {
        var invoke = () => Bus().InvokeForTenantAsync<TenantObservation>("not-a-tenant-id", new ObserveTenant(), Cancellation);

        await invoke.ShouldThrowAsync<FormatException>();
    }

    // Spike T8: each handler of one event runs in its own transaction (O3).
    [Fact]
    public async Task PublishAnEventWithTwoHandlers_OneThrows_TheOtherCommits()
    {
        var tenant = Guid.NewGuid();
        var value = $"ping-{Guid.NewGuid():N}";

        await _host.Host.TrackActivity().DoNotAssertOnExceptionsDetected().ExecuteAndWaitAsync(context =>
            context.PublishAsync(new ProbePinged(value), new DeliveryOptions { TenantId = tenant.ToString() }).AsTask());

        (await ReadProbesAsync(tenant)).ShouldBe([$"recorded:{value}"]);
    }

    // Spike T10a: Wolverine does not look at a Result, so a handler rejects before it changes anything (W7).
    [Fact]
    public async Task InvokeForTenant_HandlerRejectsAfterAChange_ChangeIsCommitted()
    {
        var tenant = Guid.NewGuid();

        var result = await Bus().InvokeForTenantAsync<Result>(tenant.ToString(), new WriteProbeThenReject("kept"), Cancellation);

        result.IsSuccess.ShouldBeFalse();
        (await ReadProbesAsync(tenant)).ShouldBe(["kept"]);
    }

    private IMessageBus Bus() => _host.Host.MessageBus();

    private async Task<string[]> ReadProbesAsync(Guid tenant)
    {
        var probes = await Bus().InvokeForTenantAsync<Result<string[]>>(tenant.ToString(), new ReadProbes(), Cancellation);
        return probes.Value;
    }

    private static string? PostgresErrorOf(Exception? exception) =>
        exception switch
        {
            null => null,
            PostgresException postgres => postgres.SqlState,
            _ => PostgresErrorOf(exception.InnerException),
        };
}
