using Npgsql;
using SharedKernel;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// A handler's messages are stored in its own transaction, so they commit and roll back with its work (O1), and they are delivered
// after the commit, under the handler's tenant (O2).
public sealed class OutboxTests(Database database) : IAsyncLifetime
{
    private PipelineHost _host = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_command_that_succeeds_with_an_event_publishes_it_under_its_tenant_after_the_commit()
    {
        var tenant = Guid.NewGuid();
        var value = $"written-{Guid.NewGuid():N}";

        await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync<Result>(tenant.ToString(), new WriteProbeAndAnnounce(value)));

        (await ReadProbesAsync(tenant)).ShouldBe([$"announced:{value}", value]);
    }

    // Spike T4: the row the handler saved and the message it sent are in one transaction, and the exception rolls back both.
    [Fact]
    public async Task InvokeForTenant_HandlerWritesSendsThenThrows_RowAndEnvelopeAreGone()
    {
        var tenant = Guid.NewGuid();
        var value = $"lost-{Guid.NewGuid():N}";

        Exception? thrown = null;

        var session = await _host.Host.TrackActivity().DoNotAssertOnExceptionsDetected().ExecuteAndWaitAsync(async Task (IMessageContext context) =>
            thrown = await Record.ExceptionAsync(() => context.InvokeForTenantAsync(tenant.ToString(), new WriteProbeThenFail(value), Cancellation)));

        thrown.ShouldBeOfType<InvalidOperationException>();
        session.Executed.MessagesOf<ProbeAnnounced>().ShouldBeEmpty();
        (await database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM probes.probes WHERE value LIKE '%{value}'")).ShouldBe(0);
        (await StoredCountAsync(value)).ShouldBe(0);
    }

    // Spike T10b.
    [Fact]
    public async Task InvokeForTenant_ValidateStops_NothingIsWrittenOrSent()
    {
        var tenant = Guid.NewGuid();
        var value = $"stopped-{Guid.NewGuid():N}";

        var session = await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync(tenant.ToString(), new WriteProbeUnlessRejected(value, Reject: true)));

        session.Executed.MessagesOf<ProbeAnnounced>().ShouldBeEmpty();
        (await ReadProbesAsync(tenant)).ShouldBeEmpty();
        (await StoredCountAsync(value)).ShouldBe(0);
    }

    [Fact]
    public async Task InvokeForTenant_ValidateContinues_ProbeIsWrittenAndAnnounced()
    {
        var tenant = Guid.NewGuid();
        var value = $"allowed-{Guid.NewGuid():N}";

        await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync(tenant.ToString(), new WriteProbeUnlessRejected(value, Reject: false)));

        (await ReadProbesAsync(tenant)).ShouldBe([$"announced:{value}", value]);
    }

    // A handler without a tenant sends its messages with Wolverine's default tenant id, which names no tenant.
    [Fact]
    public async Task A_message_cascaded_without_a_tenant_is_handled_without_one()
    {
        var session = await _host.Host.TrackActivity().ExecuteAndWaitAsync(context => context.InvokeAsync(new ReadProbesLater()));

        session.Executed.SingleMessage<ReadProbes>().ShouldNotBeNull();
    }

    private IMessageBus Bus() => _host.Host.MessageBus();

    private async Task<string[]> ReadProbesAsync(Guid tenant)
    {
        var probes = await Bus().InvokeForTenantAsync<Result<string[]>>(tenant.ToString(), new ReadProbes(), Cancellation);
        return probes.Value;
    }

    private async Task<long> StoredCountAsync(string marker)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(Cancellation);
        return await StoredMessages.CountAsync(connection, marker, Cancellation);
    }
}
