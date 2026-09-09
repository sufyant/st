using Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Admin;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

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
    }
}
