using Audit.Application.Ports;
using Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Audit.Infrastructure;

internal sealed class AuditLog(AuditDbContext audit) : IAuditLog
{
    public Task<bool> ContainsAsync(Guid entryId, CancellationToken cancellationToken) =>
        audit.Entries.AnyAsync(entry => entry.Id == entryId, cancellationToken);

    public async Task AddAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        audit.Entries.Add(entry);
        await audit.SaveChangesAsync(cancellationToken);
    }
}
