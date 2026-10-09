using Audit.Application.Ports;
using Audit.Domain;
using ControlPlane.Contracts;

namespace Audit.Application;

// The audit record of a tenant's creation, written when ControlPlane announces the tenant active, under that tenant. The system
// admin who started the onboarding acted.
public static class RecordTenantCreatedHandler
{
    private const string TenantCreated = "tenant.created";

    // An event may arrive twice; its id is the entry's id, so an entry already stored is not stored again. Should two copies race,
    // the second fails on the entry's key, and its retry finds the entry stored.
    public static async Task HandleAsync(TenantActivated activated, IAuditLog log, CancellationToken cancellationToken)
    {
        if (await log.ContainsAsync(activated.EventId, cancellationToken))
        {
            return;
        }

        await log.AddAsync(
            new AuditEntry(activated.EventId, activated.OccurredAt, activated.CreatedBy.ToString(), AuditEntryKind.Command, TenantCreated, null),
            cancellationToken);
    }
}
