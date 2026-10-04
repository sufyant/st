using Microsoft.EntityFrameworkCore.Design;
using Tenancy;

namespace ControlPlane.Infrastructure;

// Lets `dotnet ef migrations add` build the model without starting the host; it never connects.
internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(TenancyServiceCollectionExtensions.ModuleDbContextOptions<CatalogDbContext>(CatalogDbContext.Schema, "Host=design-time"));
}
