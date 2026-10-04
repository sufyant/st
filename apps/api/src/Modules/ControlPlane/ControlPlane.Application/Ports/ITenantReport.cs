using SharedKernel;

namespace ControlPlane.Application.Ports;

/// <summary>Reports across tenants for system admins, read through the read-only reporting role (0018, 0031).</summary>
public interface ITenantReport
{
    Task<PagedList<TenantSummary>> ListTenantsAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record TenantSummary(Guid Id, string Slug, string Status, int MemberCount);
