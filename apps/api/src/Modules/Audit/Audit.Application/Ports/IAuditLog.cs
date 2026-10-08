using Audit.Domain;

namespace Audit.Application.Ports;

/// <summary>The audit log of the active tenant.</summary>
/// <remarks>
/// Public only because Wolverine's generated code passes it to public handlers; its members speak domain types, so they
/// are internal to the module.
/// </remarks>
public interface IAuditLog
{
    internal Task<bool> ContainsAsync(Guid entryId, CancellationToken cancellationToken);

    internal Task AddAsync(AuditEntry entry, CancellationToken cancellationToken);
}
