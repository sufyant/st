using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantProvisioningService(AdminDbContext dbContext)
{
    public async Task<Tenant> ProvisionAsync(
        TenantSlug slug, string name, CancellationToken cancellationToken = default)
    {
        var tenant = Tenant.Create(slug, name);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        // tenant.SchemaName is already validated via TenantSlug.Create(), but this
        // call site re-checks it against the safe-identifier allowlist as
        // defense in depth before interpolating it into raw DDL.
        SafePostgresIdentifier.EnsureSafe(tenant.SchemaName, nameof(tenant.SchemaName));

#pragma warning disable EF1002 // Value is validated via SafePostgresIdentifier.EnsureSafe before this call
        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{tenant.SchemaName}\"", cancellationToken);
#pragma warning restore EF1002

        await transaction.CommitAsync(cancellationToken);

        return tenant;
    }
}
