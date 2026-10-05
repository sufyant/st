using Microsoft.EntityFrameworkCore.Design;
using Tenancy;

namespace Audit.Infrastructure;

// Lets `dotnet ef migrations add` build the model without starting the host; it never connects.
internal sealed class AuditDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(TenancyServiceCollectionExtensions.ModuleDbContextOptions<AuditDbContext>(AuditDbContext.Schema, "Host=design-time"), new TenantContext());
}
