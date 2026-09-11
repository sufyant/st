using Domain.ControlPlane.Administration;
using Domain.ControlPlane.Invitations;
using Domain.ControlPlane.Memberships;
using Infrastructure.Messaging;
using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence.ControlPlane.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.ControlPlane;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("control");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ControlPlaneDbContext).Assembly,
            type => type.Namespace == typeof(TenantConfiguration).Namespace);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<TenantId>().HaveConversion<TenantIdConverter>();
        builder.Properties<MembershipId>().HaveConversion<MembershipIdConverter>();
        builder.Properties<InvitationId>().HaveConversion<InvitationIdConverter>();
        builder.Properties<PlatformAdminId>().HaveConversion<PlatformAdminIdConverter>();
    }
}
