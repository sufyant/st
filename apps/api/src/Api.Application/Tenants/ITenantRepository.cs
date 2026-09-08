using Api.Domain;

namespace Api.Application.Tenants;

public interface ITenantRepository
{
    Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken);
}
