using Microsoft.EntityFrameworkCore.Design;
using Tenancy;

namespace Notifications.Infrastructure;

// Lets `dotnet ef migrations add` build the model without starting the host; it never connects.
internal sealed class NotificationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(
            TenancyServiceCollectionExtensions.ModuleDbContextOptions<NotificationsDbContext>(NotificationsDbContext.Schema, "Host=design-time"),
            new TenantContext());
}
