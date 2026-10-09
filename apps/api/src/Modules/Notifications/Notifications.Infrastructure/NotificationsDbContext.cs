using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace Notifications.Infrastructure;

/// <summary>The <c>notifications</c> schema, under row level security. It holds no table at present.</summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : TenantDbContext(options)
{
    public const string Schema = "notifications";

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}
