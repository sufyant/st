using Infrastructure.Persistence.Admin;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed class PostgresTenantDatabaseMigrator(
    AdminDbContext adminDbContext,
    PostgresTenantDatabaseProvisioner databaseProvisioner,
    TenantDbContextFactory tenantDbContextFactory)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var tenants = await adminDbContext.Tenants
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            await databaseProvisioner.CreateAsync(tenant, cancellationToken);
            await using var tenantDbContext = tenantDbContextFactory.Create(tenant);
            await tenantDbContext.Database.MigrateAsync(cancellationToken);
        }
    }
}
