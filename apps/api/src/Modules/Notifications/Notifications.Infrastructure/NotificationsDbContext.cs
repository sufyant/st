using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace Notifications.Infrastructure;

/// <summary>The <c>notifications</c> schema, under row level security (0014). It holds no table at present.</summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string Schema = "notifications";

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}
