using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.IntegrationTests;

// A stand-in for a module's tenant entity and its handlers, to drive the host's tenant pipeline end to end. A handler only changes
// the DbContext; Wolverine saves and commits it, unless the handler throws.
// Wolverine discovers only public handlers, messages and the types in their signatures.
public sealed class Probe : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Value { get; init; }
}

public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : TenantDbContext(options)
{
    public const string Schema = "probes";

    public DbSet<Probe> Probes => Set<Probe>();

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}

public sealed record WriteProbe(string Value);

public static class WriteProbeHandler
{
    public static Result Handle(WriteProbe command, ProbeDbContext probes)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        return Result.Success();
    }
}

// Saves its probe and sends a message, then fails: the probe and the message are lost together.
public sealed record WriteProbeThenFail(string Value);

public static class WriteProbeThenFailHandler
{
    public static async Task Handle(WriteProbeThenFail command, ProbeDbContext probes, IMessageContext messaging, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        await probes.SaveChangesAsync(cancellationToken);
        await messaging.PublishAsync(new ProbeAnnounced(command.Value));
        throw new InvalidOperationException("The handler failed after writing.");
    }
}

// A failed Result is not an exception: Wolverine commits what the handler changed before it returned (W7).
public sealed record WriteProbeThenReject(string Value);

public static class WriteProbeThenRejectHandler
{
    public static Result Handle(WriteProbeThenReject command, ProbeDbContext probes)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        return Error.Conflict("probe.rejected", "The handler rejected the probe after writing it.");
    }
}

// The way to reject a command: before the handler changes anything (W7).
public sealed record WriteProbeUnlessRejected(string Value, bool Reject);

public static class WriteProbeUnlessRejectedHandler
{
    public static HandlerContinuation Validate(WriteProbeUnlessRejected command) =>
        command.Reject ? HandlerContinuation.Stop : HandlerContinuation.Continue;

    public static ProbeAnnounced Handle(WriteProbeUnlessRejected command, ProbeDbContext probes)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        return new ProbeAnnounced(command.Value);
    }
}

// Cascades a write as a new message, which Wolverine hands on with the tenant of this one.
public sealed record WriteProbeLater(string Value);

public static class WriteProbeLaterHandler
{
    public static WriteProbe Handle(WriteProbeLater command) => new(command.Value);
}

// Cascades a message whose handler writes down the tenant its transaction declared.
public sealed record WriteTenantSettingLater;

public sealed record WriteTenantSetting;

public static class WriteTenantSettingLaterHandler
{
    public static WriteTenantSetting Handle(WriteTenantSettingLater command) => new();
}

public static class WriteTenantSettingHandler
{
    public static async Task Handle(WriteTenantSetting command, ProbeDbContext probes, CancellationToken cancellationToken) =>
        probes.Probes.Add(new Probe { Value = $"setting:{await TenantSetting.ReadAsync(probes, cancellationToken)}" });
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

// What a handler sees from inside its transaction: the tenant declared, and how many probes row level security lets it read.
public sealed record ObserveTenant;

public sealed record TenantObservation(string? Setting, int VisibleProbes);

public static class ObserveTenantHandler
{
    public static async Task<TenantObservation> Handle(ObserveTenant query, ProbeDbContext probes, CancellationToken cancellationToken) =>
        new(await TenantSetting.ReadAsync(probes, cancellationToken), await probes.Probes.CountAsync(cancellationToken));
}

// Write a probe and announce it in one handler.
public sealed record WriteProbeAndAnnounce(string Value);

public static class WriteProbeAndAnnounceHandler
{
    public static (Result, ProbeAnnounced) Handle(WriteProbeAndAnnounce command, ProbeDbContext probes)
    {
        probes.Probes.Add(new Probe { Value = command.Value });
        return (Result.Success(), new ProbeAnnounced(command.Value));
    }
}

public sealed record ProbeAnnounced(string Value);

// Records the announcement as a probe, under the tenant of the message that announced it.
public static class ProbeAnnouncedHandler
{
    public static void Handle(ProbeAnnounced message, ProbeDbContext probes) =>
        probes.Probes.Add(new Probe { Value = $"announced:{message.Value}" });
}

// One event, two handlers: each saves its own probe, and the second then fails. Each runs in its own transaction (O3).
public sealed record ProbePinged(string Value);

public static class RecordPingHandler
{
    public static async Task Handle(ProbePinged message, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = $"recorded:{message.Value}" });
        await probes.SaveChangesAsync(cancellationToken);
    }
}

public static class FailOnPingHandler
{
    public static async Task Handle(ProbePinged message, ProbeDbContext probes, CancellationToken cancellationToken)
    {
        probes.Probes.Add(new Probe { Value = $"failed:{message.Value}" });
        await probes.SaveChangesAsync(cancellationToken);
        throw new InvalidOperationException("The second handler of the event failed.");
    }
}

internal static class TenantSetting
{
    public static Task<string?> ReadAsync(ProbeDbContext probes, CancellationToken cancellationToken) =>
        probes.Database
            .SqlQuery<string?>($"SELECT NULLIF(current_setting('app.tenant_id', true), '') AS \"Value\"")
            .SingleAsync(cancellationToken);
}

// The envelopes Wolverine has stored whose body carries the marker: those waiting to be handled and those waiting to be sent.
public static class StoredMessages
{
    public static async Task<long> CountAsync(NpgsqlConnection connection, string marker, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE position(convert_to(@marker, 'UTF8') in body) > 0)
                 + (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE position(convert_to(@marker, 'UTF8') in body) > 0)
            """,
            connection);
        command.Parameters.AddWithValue("marker", marker);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
