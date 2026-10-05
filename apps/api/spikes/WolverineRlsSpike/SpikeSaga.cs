using Wolverine;

namespace WolverineRlsSpike;

public class SpikeSaga : Saga
{
    public Guid Id { get; set; }
    public int Steps { get; set; }

    // Tests 1 and 2
    public static async Task<SpikeSaga> Start(StartSaga message, SpikeDbContext db, CancellationToken ct)
    {
        db.Notes.Add(new Note { Id = message.NoteId, Text = "start" });
        await Probe.RecordAsync(message.ProbeId, db, ct);
        return new SpikeSaga { Id = message.SpikeSagaId };
    }

    // Test 3: start, then continue through a durable local queue
    public static (SpikeSaga, ContinueSaga) Start(StartAndCascade message) =>
        (new SpikeSaga { Id = message.SpikeSagaId }, new ContinueSaga(message.SpikeSagaId, message.ProbeId));

    // Tests 3 and 7
    public async Task Handle(ContinueSaga message, SpikeDbContext db, CancellationToken ct)
    {
        Steps++;
        await Probe.RecordAsync(message.ProbeId, db, ct);
    }

    public static void NotFound(ContinueSaga message) => Probe.MarkNotFound(message.ProbeId);

    // Test 4: real writes inside the transaction, a cascaded message in the outbox, then an exception
    public async Task Handle(FailAfterWork message, SpikeDbContext db, IMessageContext bus, CancellationToken ct)
    {
        db.Notes.Add(new Note { Id = message.NoteId, Text = "fail" });
        Steps++;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(new ContinueSaga(Id, message.ProbeId));
        throw new InvalidOperationException("boom after work");
    }

    // Test 9
    public async Task Handle(Bump message)
    {
        await BumpGate.WaitAsync();
        Steps++;
    }
}
