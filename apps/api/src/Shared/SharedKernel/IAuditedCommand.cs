namespace SharedKernel;

/// <summary>
/// A state-changing command whose success is recorded in the audit log (0040). The host's pipeline writes the record to the outbox
/// in the command's own tenant transaction, so it is kept or lost with the command's work.
/// </summary>
public interface IAuditedCommand
{
    /// <summary>The identity provider's id of the user who sends the command.</summary>
    string ActorId { get; }

    /// <summary>
    /// What the record keeps of the command: the ids and values worth keeping for years. Never a credential such as an invitation
    /// token, because the record is a stored message and then a row kept for years.
    /// </summary>
    object? AuditDetails { get; }
}
