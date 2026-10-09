using Audit.Application.Ports;
using Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Audit.Infrastructure;

/// <summary>The audit log on the module's DbContext.</summary>
/// <remarks>Public only so that Wolverine's generated code can build it on the handler's own DbContext (W1, W9).</remarks>
public sealed class AuditLog(AuditDbContext audit) : IAuditLog
{
    Task<bool> IAuditLog.ContainsAsync(Guid entryId, CancellationToken cancellationToken) =>
        audit.Entries.AnyAsync(entry => entry.Id == entryId, cancellationToken);

    async Task IAuditLog.AddAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        audit.Entries.Add(entry);
        await audit.SaveChangesAsync(cancellationToken);
    }
}
