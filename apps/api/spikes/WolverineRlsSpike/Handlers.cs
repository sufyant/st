using SharedKernel;
using Wolverine;

namespace WolverineRlsSpike;

// Test 6
public static class UntenantedWriteHandler
{
    public static async Task Handle(UntenantedWrite message, SpikeDbContext db, CancellationToken ct)
    {
        await Probe.RecordAsync(message.ProbeId, db, ct);
        db.Notes.Add(new Note { Id = message.NoteId, Text = "untenanted" });
    }
}

// Test 8: two handlers for the same message, each with its own DbContext and schema. This one fails.
public static class NoteOnPingHandler
{
    public static void Handle(Ping message, SpikeDbContext db)
    {
        db.Notes.Add(new Note { Id = message.NoteId, Text = "ping" });
        throw new InvalidOperationException("note handler fails");
    }
}

public static class AuditOnPingHandler
{
    public static void Handle(Ping message, AuditDbContext db) =>
        db.Entries.Add(new AuditEntry { Id = message.EntryId, Text = "ping" });
}

// Test 10: a failed Result after a change
public static class ResultWriteHandler
{
    public static Result Handle(ResultWrite message, SpikeDbContext db)
    {
        db.Notes.Add(new Note { Id = message.NoteId, Text = "result" });
        return Result.Failure(Error.Validation("spike.failed", "Handler decided to fail."));
    }
}

// Test 10, the stock way to stop: decide before the handler runs.
public static class ValidatedResultWriteHandler
{
    public static HandlerContinuation Validate(ValidatedResultWrite message) =>
        message.Fail ? HandlerContinuation.Stop : HandlerContinuation.Continue;

    public static Result Handle(ValidatedResultWrite message, SpikeDbContext db)
    {
        db.Notes.Add(new Note { Id = message.NoteId, Text = "validated" });
        return Result.Success();
    }
}
