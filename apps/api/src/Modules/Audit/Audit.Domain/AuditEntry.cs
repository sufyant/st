using SharedKernel;

namespace Audit.Domain;

/// <summary>
/// One record of the audit log: who did what, when, in which tenant. Records are business data kept for years, and they are
/// never changed once written.
/// </summary>
internal sealed class AuditEntry(Guid id, DateTimeOffset occurredAt, string actorId, AuditEntryKind kind, string operation, string? details)
    : ITenantEntity
{
    public const int ActorIdMaxLength = 255;

    public const int OperationMaxLength = 500;

    public Guid Id { get; private init; } = id;

    public DateTimeOffset OccurredAt { get; private init; } = occurredAt;

    public string ActorId { get; private init; } = actorId;

    public AuditEntryKind Kind { get; private init; } = kind;

    public string Operation { get; private init; } = operation;

    public string? Details { get; private init; } = details;
}

internal enum AuditEntryKind
{
    Command,
}
