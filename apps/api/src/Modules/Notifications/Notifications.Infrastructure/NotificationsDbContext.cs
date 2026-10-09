using Microsoft.EntityFrameworkCore;
using Tenancy;
using Wolverine;

namespace Notifications.Infrastructure;

/// <summary>The <c>notifications</c> schema, under row level security. It holds no table at present.</summary>
/// <remarks>Public because Wolverine's generated code creates a module DbContext for the handlers (W9).</remarks>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, IMessageContext? messaging = null)
    : TenantDbContext(options, messaging)
{
    public const string Schema = "notifications";

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}
