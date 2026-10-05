using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace WolverineRlsSpike;

public sealed record Observation(string? TenantSetting, int VisibleNotes, string? MessagingTenant);

// What a handler saw from inside Wolverine's transaction, keyed per test.
public static class Probe
{
    private static readonly ConcurrentDictionary<Guid, Observation> Observations = new();
    private static readonly ConcurrentDictionary<Guid, bool> NotFound = new();

    public static async Task RecordAsync(Guid key, SpikeDbContext db, CancellationToken ct)
    {
        var setting = await db.Database
            .SqlQuery<string?>($"SELECT current_setting('app.tenant_id', true) AS \"Value\"")
            .SingleAsync(ct);
        var visible = await db.Notes.CountAsync(ct);
        Observations[key] = new Observation(setting, visible, db.CurrentTenantId);
    }

    public static Observation? Get(Guid key) => Observations.GetValueOrDefault(key);

    public static void MarkNotFound(Guid key) => NotFound[key] = true;

    public static bool WasNotFound(Guid key) => NotFound.ContainsKey(key);
}

// Test 9: holds the first two Bump handlers until both have loaded the saga, so they really overlap.
public static class BumpGate
{
    private static int _arrivals;
    private static TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int Arrivals => Volatile.Read(ref _arrivals);

    public static void Reset()
    {
        _arrivals = 0;
        _both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static async Task WaitAsync()
    {
        var arrival = Interlocked.Increment(ref _arrivals);
        if (arrival > 2)
        {
            return; // retries pass straight through
        }

        if (arrival == 2)
        {
            _both.TrySetResult();
        }

        await _both.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
