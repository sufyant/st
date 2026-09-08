using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Api.Infrastructure;

public sealed class TenantMigrationRunner(AdminDbContext adminDbContext, string connectionString)
{
    public async Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        // SchemaName is an EF Ignore()'d computed property, so this projection only works
        // because it's evaluated client-side after materialization - a .Where() on it
        // would throw at runtime instead of translating to SQL.
        var schemaNames = await adminDbContext.Tenants
            .AsNoTracking()
            .Select(t => t.SchemaName)
            .ToListAsync(cancellationToken);

        foreach (var schemaName in schemaNames)
        {
            await MigrateTenantAsync(schemaName, cancellationToken);
        }
    }

    public async Task MigrateTenantAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        SafePostgresIdentifier.EnsureSafe(schemaName, nameof(schemaName));

        // Npgsql defaults the __EFMigrationsHistory table to the "public" schema
        // regardless of TenantDbContext.OnModelCreating's HasDefaultSchema() call, so
        // it must be routed to the tenant schema explicitly here - otherwise every
        // tenant's migration history would collide in one shared public table.
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
            .ReplaceService<IModelCacheKeyFactory, SchemaAwareModelCacheKeyFactory>()
            .Options;

        await using var tenantContext = new TenantDbContext(options, schemaName);
        await tenantContext.Database.MigrateAsync(cancellationToken);
    }
}
