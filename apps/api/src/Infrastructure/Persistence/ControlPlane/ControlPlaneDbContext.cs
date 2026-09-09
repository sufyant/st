using Domain.Access;
using Infrastructure.Messaging;
using Domain.Tenants;
using Infrastructure.Persistence.ControlPlane.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.ControlPlane;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("control");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ControlPlaneDbContext).Assembly,
            type => type.Namespace == typeof(TenantConfiguration).Namespace);
    }
}
