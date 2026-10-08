using Npgsql;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// A tenant command's messages are stored in its own transaction, so they commit and roll back with its work. An
// expected failure undoes the work and discards the messages, just as an exception does.
public sealed class OutboxTests(Database database) : IAsyncLifetime
{
    private PipelineHost _host = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_message_published_by_a_tenant_command_is_stored_in_the_commands_transaction()
    {
        var tenant = Guid.NewGuid();
        var marker = $"marker-{Guid.NewGuid():N}";

        var counts = await Bus().InvokeForTenantAsync<SharedKernel.Result<StoredCounts>>(
            tenant.ToString(), new PublishAndCountStored(marker), Cancellation);

        counts.Value.ShouldBe(new StoredCounts(Inside: 1, Outside: 0));
    }

    [Fact]
    public async Task A_message_published_by_a_tenant_command_is_handled_under_its_tenant_once_the_command_commits()
    {
        var tenant = Guid.NewGuid();
        var marker = $"marker-{Guid.NewGuid():N}";

        await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync<SharedKernel.Result<StoredCounts>>(tenant.ToString(), new PublishAndCountStored(marker)));

        (await ReadProbesAsync(tenant)).ShouldBe([$"announced:{marker}"]);
    }

    [Fact]
    public async Task A_command_that_succeeds_with_an_event_publishes_it_under_its_tenant_after_the_commit()
    {
        var tenant = Guid.NewGuid();
        var value = $"written-{Guid.NewGuid():N}";

        await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync<SharedKernel.Result>(tenant.ToString(), new WriteProbeAndAnnounce(value)));

        (await ReadProbesAsync(tenant)).ShouldBe([$"announced:{value}", value]);
    }

    [Fact]
    public async Task A_command_that_fails_with_an_event_leaves_nothing_written_and_publishes_nothing()
    {
        var tenant = Guid.NewGuid();
        var value = $"rejected-{Guid.NewGuid():N}";

        var session = await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync<SharedKernel.Result>(tenant.ToString(), new WriteProbeThenRejectAndAnnounce(value)));

        session.Executed.MessagesOf<ProbeAnnounced>().ShouldBeEmpty();
        (await ReadProbesAsync(tenant)).ShouldBeEmpty();
        (await StoredCountAsync(value)).ShouldBe(0);
    }

    [Fact]
    public async Task A_command_that_fails_with_a_value_and_an_event_leaves_nothing_written_and_publishes_nothing()
    {
        var tenant = Guid.NewGuid();
        var value = $"rejected-{Guid.NewGuid():N}";

        var session = await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeForTenantAsync<SharedKernel.Result<Guid>>(tenant.ToString(), new WriteProbeThenRejectWithValueAndAnnounce(value)));

        session.Executed.MessagesOf<ProbeAnnounced>().ShouldBeEmpty();
        (await ReadProbesAsync(tenant)).ShouldBeEmpty();
        (await StoredCountAsync(value)).ShouldBe(0);
    }

    [Fact]
    public async Task A_message_without_a_tenant_that_fails_with_an_event_publishes_nothing()
    {
        var value = $"rejected-{Guid.NewGuid():N}";

        var session = await _host.Host.TrackActivity().ExecuteAndWaitAsync(context =>
            context.InvokeAsync<SharedKernel.Result>(new RejectAndAnnounce(value)));

        session.Executed.MessagesOf<ProbeAnnounced>().ShouldBeEmpty();
        (await StoredCountAsync(value)).ShouldBe(0);
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
        var probes = await Bus().InvokeForTenantAsync<SharedKernel.Result<string[]>>(tenant.ToString(), new ReadProbes(), Cancellation);
        return probes.Value;
    }

    private async Task<long> StoredCountAsync(string marker)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(Cancellation);
        return await StoredMessages.CountAsync(connection, marker, Cancellation);
    }
}
