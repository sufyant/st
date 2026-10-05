using Audit.Application.Ports;
using Audit.Contracts;
using Audit.Domain;

namespace Audit.Application;

public static class RecordAuditEntryHandler
{
    // A message may arrive twice (0024); an entry already stored is not stored again. Should two copies race, the second fails on
    // the entry's key, and its retry finds the entry stored.
    public static async Task HandleAsync(RecordAuditEntry message, IAuditLog log, CancellationToken cancellationToken)
    {
        if (await log.ContainsAsync(message.EntryId, cancellationToken))
        {
            return;
        }

        await log.AddAsync(
            new AuditEntry(message.EntryId, message.OccurredAt, message.ActorId, KindOf(message.Kind), message.Operation, message.Details),
            cancellationToken);
    }

    private static AuditEntryKind KindOf(AuditKind kind) => kind switch
    {
        AuditKind.Command => AuditEntryKind.Command,
        AuditKind.Denied => AuditEntryKind.Denied,
        AuditKind.SystemAdminEntry => AuditEntryKind.SystemAdminEntry,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An audit entry has a known kind."),
    };
}
