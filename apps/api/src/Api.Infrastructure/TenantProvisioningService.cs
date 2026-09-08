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

        // tenant.SchemaName is derived from TenantSlug, whose Create()
        // already restricts input to lowercase letters/digits/hyphens
        // (Phase 2a) — safe to interpolate directly into DDL, it can
        // never contain quotes or SQL metacharacters.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{tenant.SchemaName}\"", cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return tenant;
    }
}
