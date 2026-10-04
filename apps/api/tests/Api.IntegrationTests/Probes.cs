using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.IntegrationTests;

// A stand-in for a module's tenant entity and its handlers, to drive the host's tenant pipeline end to end.
// Wolverine discovers only public handlers, messages and the types in their signatures (0047).
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
