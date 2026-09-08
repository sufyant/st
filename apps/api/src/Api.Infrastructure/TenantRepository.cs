using Api.Application.Tenants;
using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantRepository(AdminDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
}
