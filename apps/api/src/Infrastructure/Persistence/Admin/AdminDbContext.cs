using Domain.Access;
using Domain.Tenants;
using Infrastructure.Persistence.Admin.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Admin;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("admin");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AdminDbContext).Assembly,
            type => type.Namespace == typeof(TenantConfiguration).Namespace);
    }
}
