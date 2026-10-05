using Microsoft.EntityFrameworkCore;
using Notifications.Domain;
using Tenancy;

namespace Notifications.Infrastructure;

/// <summary>The <c>notifications</c> schema: user-defined scheduled notifications, under row level security (0014, 0027).</summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string Schema = "notifications";

    public DbSet<ScheduledNotification> ScheduledNotifications => Set<ScheduledNotification>();

    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ScheduledNotification>(notification =>
        {
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.RecipientId).HasMaxLength(ScheduledNotification.RecipientIdMaxLength);
            notification.Property(n => n.Title).HasMaxLength(ScheduledNotification.TitleMaxLength);
            notification.Property(n => n.Body).HasMaxLength(ScheduledNotification.BodyMaxLength);
            notification.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
            notification.HasIndex(n => new { n.Status, n.DueAt });
            notification.HasIndex(n => n.RecipientId);
        });
    }
}
