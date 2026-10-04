using ControlPlane.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;
using Tenancy;

namespace ControlPlane.Infrastructure;

// Reports across tenants read the catalog as the read-only reporting role, over its own connection (0018, 0019), never as the
// application. The setting is checked on first use, as the pooled one is.
internal sealed class TenantReport(IConfiguration configuration) : ITenantReport
{
    public const string Connection = "Reporting";

    public async Task<PagedList<TenantSummary>> ListTenantsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var catalog = new CatalogDbContext(
            TenancyServiceCollectionExtensions.ModuleDbContextOptions<CatalogDbContext>(CatalogDbContext.Schema, ConnectionString));

        var total = await catalog.Tenants.LongCountAsync(cancellationToken);
        var rows = await catalog.Tenants.AsNoTracking()
            .OrderBy(tenant => tenant.Slug)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(tenant => new
            {
                tenant.Id,
                tenant.Slug,
                tenant.Status,
                Members = catalog.Memberships.Count(membership => membership.TenantId == tenant.Id),
            })
            .ToListAsync(cancellationToken);

        return new([.. rows.Select(row => new TenantSummary(row.Id, row.Slug, row.Status.ToString(), row.Members))], page, pageSize, total);
    }

    private string ConnectionString =>
        configuration.GetConnectionString(Connection) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException($"ConnectionStrings:{Connection} must name the reporting role's connection.");
}
