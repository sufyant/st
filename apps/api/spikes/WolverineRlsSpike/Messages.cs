namespace WolverineRlsSpike;

public record StartSaga(Guid SpikeSagaId, Guid NoteId, Guid ProbeId);
public record StartAndCascade(Guid SpikeSagaId, Guid ProbeId);
public record ContinueSaga(Guid SpikeSagaId, Guid ProbeId);
public record FailAfterWork(Guid SpikeSagaId, Guid NoteId, Guid ProbeId);
public record Bump(Guid SpikeSagaId);
public record UntenantedWrite(Guid NoteId, Guid ProbeId);
public record Ping(Guid NoteId, Guid EntryId);
public record ResultWrite(Guid NoteId);
public record ValidatedResultWrite(Guid NoteId, bool Fail);
