using Domain.Access;
using Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Admin;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("admin");

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasKey(x => x.Id);
            tenant.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            tenant.Property(x => x.Alias)
                .HasColumnName("alias")
                .HasMaxLength(63)
                .HasConversion(
                    alias => alias.Value,
                    value => TenantAlias.Create(value))
                .IsRequired();
            tenant.HasIndex(x => x.Alias).IsUnique();
            tenant.Ignore(x => x.SchemaName);
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.HasKey(x => x.Id);
            membership.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            membership.Property(x => x.TenantId)
                .HasColumnName("tenant_id")
                .IsRequired();
            membership.Property(x => x.ExternalUserId)
                .HasColumnName("external_user_id")
                .HasMaxLength(255)
                .HasConversion(
                    userId => userId.Value,
                    value => ExternalUserId.Create(value))
                .IsRequired();
            membership.HasIndex(x => new { x.TenantId, x.ExternalUserId }).IsUnique();
            membership.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
