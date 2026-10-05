using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Wolverine;
using Wolverine.Tracking;

namespace WolverineRlsSpike;

[Collection(SpikeGroup.Name)]
public sealed class RlsTests(SpikeFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private Database Db => fixture.Db;
    private IMessageBus Bus => fixture.Host.MessageBus();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Guid NewTenant() => Guid.NewGuid();

    private async Task StartSagaAsync(Guid tenant, Guid sagaId) =>
        await Bus.InvokeForTenantAsync(tenant.ToString(), new StartSaga(sagaId, Guid.NewGuid(), Guid.NewGuid()), Ct);

    private Task<Guid?> NoteTenantAsync(Guid noteId) =>
        Db.ScalarAsync<Guid?>("SELECT tenant_id FROM app.notes WHERE id = $1", noteId);

    private Task<int?> SagaColumnAsync(Guid sagaId, string column) =>
        Db.ScalarAsync<int?>($"SELECT {column} FROM app.spike_sagas WHERE id = $1", sagaId);

    [Fact]
    public async Task T01_saga_start_writes_note_and_saga_row_under_the_tenant()
    {
        var tenant = NewTenant();
        var sagaId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        await Bus.InvokeForTenantAsync(tenant.ToString(), new StartSaga(sagaId, noteId, Guid.NewGuid()), Ct);

        (await NoteTenantAsync(noteId)).ShouldBe(tenant);
        (await Db.ScalarAsync<Guid?>("SELECT tenant_id FROM app.spike_sagas WHERE id = $1", sagaId)).ShouldBe(tenant);
    }

    [Fact]
    public async Task T02_inside_the_handler_another_tenants_note_is_not_visible()
    {
        var tenantA = NewTenant();
        var tenantB = NewTenant();
        await Db.ExecuteAsync("INSERT INTO app.notes (id, text, tenant_id) VALUES ($1, 'a', $2)", Guid.NewGuid(), tenantA);
        await Db.ExecuteAsync("INSERT INTO app.notes (id, text, tenant_id) VALUES ($1, 'b', $2)", Guid.NewGuid(), tenantB);
        var probe = Guid.NewGuid();

        await Bus.InvokeForTenantAsync(tenantA.ToString(), new StartSaga(Guid.NewGuid(), Guid.NewGuid(), probe), Ct);

        var seen = Probe.Get(probe).ShouldNotBeNull();
        seen.TenantSetting.ShouldBe(tenantA.ToString());
        seen.VisibleNotes.ShouldBe(1); // A's own seeded note, not B's
    }

    [Fact]
    public async Task T03_saga_continues_through_a_durable_local_queue_under_the_same_tenant()
    {
        var tenant = NewTenant();
        var sagaId = Guid.NewGuid();
        var probe = Guid.NewGuid();

        await fixture.Host.TrackActivity().Timeout(Wait)
            .ExecuteAndWaitAsync((IMessageContext c) =>
                c.InvokeForTenantAsync(tenant.ToString(), new StartAndCascade(sagaId, probe), Ct));

        var seen = Probe.Get(probe).ShouldNotBeNull();
        seen.MessagingTenant.ShouldBe(tenant.ToString());
        seen.TenantSetting.ShouldBe(tenant.ToString());
        (await SagaColumnAsync(sagaId, "steps")).ShouldBe(1);
        (await SagaColumnAsync(sagaId, "version")).ShouldBe(1);
    }

    [Fact]
    public async Task T04_exception_after_work_rolls_back_note_saga_change_and_cascaded_envelope()
    {
        var tenant = NewTenant();
        var sagaId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var probe = Guid.NewGuid();
        await StartSagaAsync(tenant, sagaId);

        Exception? thrown = null;
        await fixture.Host.TrackActivity().Timeout(Wait).DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(async Task (IMessageContext c) => thrown = await Record.ExceptionAsync(() =>
                c.InvokeForTenantAsync(tenant.ToString(), new FailAfterWork(sagaId, noteId, probe), Ct)));

        thrown.ShouldBeOfType<InvalidOperationException>();

        (await NoteTenantAsync(noteId)).ShouldBeNull();
        (await SagaColumnAsync(sagaId, "steps")).ShouldBe(0);
        (await SagaColumnAsync(sagaId, "version")).ShouldBe(0);
        (await Db.ScalarAsync<long>(
            """
            SELECT (SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE convert_from(body, 'UTF8') LIKE $1)
                 + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE convert_from(body, 'UTF8') LIKE $1)
            """, $"%{probe}%")).ShouldBe(0);
        Probe.Get(probe).ShouldBeNull();
    }

    [Fact]
    public async Task T05_pooled_connection_carries_no_tenant_after_a_tenant_message()
    {
        using var host = await SpikeHost.StartAsync(Db.AppConnectionString, maxPoolSize: 1);
        var tenant = NewTenant();
        var noteId = Guid.NewGuid();

        await host.MessageBus().InvokeForTenantAsync(tenant.ToString(), new StartSaga(Guid.NewGuid(), noteId, Guid.NewGuid()), Ct);
        (await NoteTenantAsync(noteId)).ShouldBe(tenant); // the tenant really was set on that one connection

        var dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();
        await using (var cmd = dataSource.CreateCommand("SELECT current_setting('app.tenant_id', true)"))
        {
            var setting = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            (setting is null or DBNull || (string)setting == "").ShouldBeTrue($"leaked setting: {setting}");
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task T06_message_without_tenant_reads_nothing_and_cannot_insert()
    {
        await Db.ExecuteAsync("INSERT INTO app.notes (id, text, tenant_id) VALUES ($1, 'x', $2)", Guid.NewGuid(), NewTenant());
        var noteId = Guid.NewGuid();
        var probe = Guid.NewGuid();

        var ex = await Should.ThrowAsync<Exception>(() => Bus.InvokeAsync(new UntenantedWrite(noteId, probe), Ct));
        TestContext.Current.TestOutputHelper?.WriteLine(ex.ToString());

        var seen = Probe.Get(probe).ShouldNotBeNull();
        seen.VisibleNotes.ShouldBe(0);
        string.IsNullOrEmpty(seen.TenantSetting).ShouldBeTrue();
        (await NoteTenantAsync(noteId)).ShouldBeNull();
        (await Db.ScalarAsync<long>("SELECT count(*) FROM app.notes WHERE id = $1", noteId)).ShouldBe(0);
        FindPostgresException(ex).ShouldNotBeNull().SqlState.ShouldBeOneOf("42501", "23502");
    }

    [Fact]
    public async Task T07_saga_of_tenant_A_is_not_loaded_under_tenant_B()
    {
        var tenantA = NewTenant();
        var sagaId = Guid.NewGuid();
        var probe = Guid.NewGuid();
        await StartSagaAsync(tenantA, sagaId);

        await Bus.InvokeForTenantAsync(NewTenant().ToString(), new ContinueSaga(sagaId, probe), Ct);

        Probe.WasNotFound(probe).ShouldBeTrue();
        Probe.Get(probe).ShouldBeNull();
        (await SagaColumnAsync(sagaId, "steps")).ShouldBe(0);
    }

    [Fact]
    public async Task T08_separated_handlers_commit_independently()
    {
        var tenant = NewTenant();
        var ping = new Ping(Guid.NewGuid(), Guid.NewGuid());

        await fixture.Host.TrackActivity().Timeout(Wait).DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync((IMessageContext c) =>
                c.PublishAsync(ping, new DeliveryOptions { TenantId = tenant.ToString() }).AsTask());

        (await Db.ScalarAsync<Guid?>("SELECT tenant_id FROM audit.entries WHERE id = $1", ping.EntryId)).ShouldBe(tenant);
        (await NoteTenantAsync(ping.NoteId)).ShouldBeNull();
    }

    [Fact]
    public async Task T09_concurrent_saga_updates_hit_the_version_check_and_retry_without_lost_update()
    {
        var tenant = NewTenant();
        var sagaId = Guid.NewGuid();
        await StartSagaAsync(tenant, sagaId);
        BumpGate.Reset();

        await Task.WhenAll(
            Bus.InvokeForTenantAsync(tenant.ToString(), new Bump(sagaId), Ct),
            Bus.InvokeForTenantAsync(tenant.ToString(), new Bump(sagaId), Ct));

        BumpGate.Arrivals.ShouldBe(3); // two overlapping runs + exactly one retry
        (await SagaColumnAsync(sagaId, "steps")).ShouldBe(2);
        (await SagaColumnAsync(sagaId, "version")).ShouldBe(2);
    }

    [Fact]
    public async Task T10a_failed_result_after_a_change_is_committed()
    {
        var tenant = NewTenant();
        var noteId = Guid.NewGuid();

        var result = await Bus.InvokeForTenantAsync<Result>(tenant.ToString(), new ResultWrite(noteId), Ct);

        result.IsSuccess.ShouldBeFalse();
        (await NoteTenantAsync(noteId)).ShouldBe(tenant); // recorded behaviour: the change commits
    }

    [Fact]
    public async Task T10b_validate_stops_the_handler_before_any_change()
    {
        var tenant = NewTenant();
        var stopped = Guid.NewGuid();
        var allowed = Guid.NewGuid();

        await Bus.InvokeForTenantAsync(tenant.ToString(), new ValidatedResultWrite(stopped, Fail: true), Ct);
        await Bus.InvokeForTenantAsync(tenant.ToString(), new ValidatedResultWrite(allowed, Fail: false), Ct);

        (await NoteTenantAsync(stopped)).ShouldBeNull();
        (await NoteTenantAsync(allowed)).ShouldBe(tenant);
    }

    private static PostgresException? FindPostgresException(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException pg)
            {
                return pg;
            }
        }

        return null;
    }
}
