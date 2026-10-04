using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

/// <summary>The <c>catalog</c> schema: the control plane's data above tenants, without row level security (0021).</summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Id).ValueGeneratedNever();
            tenant.Property(t => t.Slug).HasMaxLength(63);
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.ExternalId).HasMaxLength(255);
            user.HasIndex(u => u.ExternalId).IsUnique();
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.HasKey(m => new { m.TenantId, m.UserId });
            membership.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId);
        });
    }
}
