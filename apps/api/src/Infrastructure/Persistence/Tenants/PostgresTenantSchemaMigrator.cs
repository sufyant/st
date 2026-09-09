using Infrastructure.Persistence.Admin;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed class PostgresTenantSchemaMigrator(
    AdminDbContext adminDbContext,
    PostgresTenantSchemaProvisioner schemaProvisioner,
    TenantDbContextFactory tenantDbContextFactory)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await adminDbContext.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            await schemaProvisioner.CreateAsync(tenantId, cancellationToken);
            await using var tenantDbContext = tenantDbContextFactory.Create(tenantId);
            await tenantDbContext.Database.MigrateAsync(cancellationToken);
        }
    }
}
