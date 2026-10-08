namespace Audit.Contracts;

/// <summary>
/// An audit record to store (0040). It is sent through the outbox in the tenant where the audited event happened, and the Audit
/// module stores it under that tenant. The sender chooses the entry's id, so a message that arrives twice is stored once (0024).
/// </summary>
/// <param name="ActorId">The identity provider's id of the user who acted.</param>
/// <param name="Operation">What was done: a command's name.</param>
/// <param name="Details">What the record keeps beyond that, as JSON, or nothing.</param>
public sealed record RecordAuditEntry(
    Guid EntryId,
    DateTimeOffset OccurredAt,
    string ActorId,
    AuditKind Kind,
    string Operation,
    string? Details);

public enum AuditKind
{
    /// <summary>A state-changing command that succeeded.</summary>
    Command,
}
