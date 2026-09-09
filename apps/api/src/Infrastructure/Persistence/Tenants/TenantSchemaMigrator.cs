using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantSchemaMigrator(TenantDbContextFactory contextFactory)
{
    public async Task MigrateAsync(string databaseName, CancellationToken cancellationToken)
    {
        // EF Core creates a missing database on Migrate, which would silently mask a lost tenant.
        if (!await contextFactory.DatabaseExistsAsync(databaseName, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Tenant database '{databaseName}' does not exist. Provisioning creates tenant databases; migration never does.");
        }

        await using var context = contextFactory.Create(databaseName);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
