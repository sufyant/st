using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.IntegrationTests;

// A stand-in for a module's tenant entity and its handlers, to drive the host's tenant pipeline end to end.
// Wolverine discovers only public handlers, messages and the types in their signatures.
public sealed class Probe : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Value { get; init; }
}

public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, TenantContext tenant) : TenantDbContext(options, tenant)
{
    public const string Schema = "probes";

    public DbSet<Probe> Probes => Set<Probe>();

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}

public sealed record WriteProbe(string Value);

public static class WriteProbeHandler
{
    public static async Task<Result> Handle(WriteProbe command, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record WriteProbeThenFail(string Value);

public static class WriteProbeThenFailHandler
{
    public static async Task Handle(WriteProbeThenFail command, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        throw new InvalidOperationException("The handler failed after writing.");
    }
}

public sealed record WriteProbeThenReject(string Value);

public static class WriteProbeThenRejectHandler
{
    public static async Task<Result> Handle(WriteProbeThenReject command, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return Error.Conflict("probe.rejected", "The handler rejected the probe after writing it.");
    }
}

public sealed record WriteProbeThenRejectWithValue(string Value);

public static class WriteProbeThenRejectWithValueHandler
{
    public static async Task<Result<Guid>> Handle(WriteProbeThenRejectWithValue command, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return Error.Conflict("probe.rejected", "The handler rejected the probe after writing it.");
    }
}

// Cascades a write as a new message, which Wolverine hands on with the tenant of this one.
public sealed record WriteProbeLater(string Value);

public static class WriteProbeLaterHandler
{
    public static WriteProbe Handle(WriteProbeLater command) => new(command.Value);
}

public sealed record ReadProbes;

// Cascades a read; a message without a tenant hands it on with Wolverine's default tenant id.
public sealed record ReadProbesLater;

public static class ReadProbesLaterHandler
{
    public static ReadProbes Handle(ReadProbesLater command) => new();
}

public static class ReadProbesHandler
{
    public static async Task<Result<string[]>> Handle(ReadProbes query, ProbeDbContext probes, CancellationToken cancellationToken) =>
        await probes.Probes.Select(probe => probe.Value).OrderBy(value => value).ToArrayAsync(cancellationToken);
}

public sealed record ReadTenantSetting;

public static class ReadTenantSettingHandler
{
    public static async Task<string?> Handle(ReadTenantSetting query, TenantTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection.CreateCommand();
        command.CommandText = "SELECT NULLIF(current_setting('app.tenant_id', true), '')";
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }
}

// Publishes a message, then counts its stored envelope from inside the command's transaction and from a connection of its own.
public sealed record PublishAndCountStored(string Marker);

public sealed record StoredCounts(long Inside, long Outside);

public static class PublishAndCountStoredHandler
{
    public static async Task<Result<StoredCounts>> Handle(
        PublishAndCountStored command,
        IMessageBus bus,
        TenantTransaction transaction,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await bus.PublishAsync(new ProbeAnnounced(command.Marker));

        var inside = await StoredMessages.CountAsync(transaction.Connection, command.Marker, cancellationToken);
        await using var outside = await dataSource.OpenConnectionAsync(cancellationToken);
        return new StoredCounts(inside, await StoredMessages.CountAsync(outside, command.Marker, cancellationToken));
    }
}

// Write a probe and announce it in one handler, which reports success or rejects the probe afterwards.
public sealed record WriteProbeAndAnnounce(string Value);

public static class WriteProbeAndAnnounceHandler
{
    public static async Task<(Result, ProbeAnnounced)> Handle(WriteProbeAndAnnounce command, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return (Result.Success(), new ProbeAnnounced(command.Value));
    }
}

public sealed record WriteProbeThenRejectAndAnnounce(string Value);

public static class WriteProbeThenRejectAndAnnounceHandler
{
    public static async Task<(Result, ProbeAnnounced)> Handle(
        WriteProbeThenRejectAndAnnounce command,
        ProbeDbContext probes,
        CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return (Error.Conflict("probe.rejected", "The handler rejected the probe after writing it."), new ProbeAnnounced(command.Value));
    }
}

public sealed record WriteProbeThenRejectWithValueAndAnnounce(string Value);

public static class WriteProbeThenRejectWithValueAndAnnounceHandler
{
    public static async Task<(Result<Guid>, ProbeAnnounced)> Handle(
        WriteProbeThenRejectWithValueAndAnnounce command,
        ProbeDbContext probes,
        CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        return (Error.Conflict("probe.rejected", "The handler rejected the probe after writing it."), new ProbeAnnounced(command.Value));
    }
}

// Rejects without writing anything, for a message that carries no tenant.
public sealed record RejectAndAnnounce(string Value);

public static class RejectAndAnnounceHandler
{
    public static (Result, ProbeAnnounced) Handle(RejectAndAnnounce command) =>
        (Error.Conflict("probe.rejected", "The handler rejected the probe."), new ProbeAnnounced(command.Value));
}

public sealed record ProbeAnnounced(string Value);

// Records the announcement as a probe, under the tenant of the message that announced it.
public static class ProbeAnnouncedHandler
{
    public static async Task Handle(ProbeAnnounced message, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = $"announced:{message.Value}" });
        await probes.SaveChangesAsync(cancellationToken);
    }
}

// The envelopes Wolverine has stored whose body carries the marker.
public static class StoredMessages
{
    public static async Task<long> CountAsync(NpgsqlConnection connection, string marker, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE position(convert_to(@marker, 'UTF8') in body) > 0",
            connection);
        command.Parameters.AddWithValue("marker", marker);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
