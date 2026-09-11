using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed record TenantMigrationOutcome(string Alias, string Result, string? Error = null);

public sealed class TenantMigrationRunner(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantSchemaMigrator schemaMigrator)
{
    public const string Migrated = "migrated";
    public const string Skipped = "skipped";
    public const string Failed = "failed";

    public async Task<IReadOnlyList<TenantMigrationOutcome>> RunAsync(
        string? alias,
        CancellationToken cancellationToken)
    {
        var query = controlPlaneDbContext.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(alias))
        {
            var tenantAlias = TenantAlias.Create(alias);
            query = query.Where(tenant => tenant.Alias == tenantAlias);
        }

        var tenants = await query.OrderBy(tenant => tenant.CreatedAt).ToListAsync(cancellationToken);
        var outcomes = new List<TenantMigrationOutcome>();

        foreach (var tenant in tenants)
        {
            if (tenant.Status is not (TenantStatus.Active or TenantStatus.Suspended))
            {
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, Skipped));
                continue;
            }

            try
            {
                await schemaMigrator.MigrateAsync(tenant.DatabaseName.Value, cancellationToken);
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, Migrated));
            }
            catch (Exception exception)
            {
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, Failed, exception.Message));
            }
        }

        return outcomes;
    }
}
