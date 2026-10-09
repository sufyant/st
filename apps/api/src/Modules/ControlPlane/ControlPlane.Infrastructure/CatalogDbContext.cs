using ControlPlane.Application.Tenants;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.SystemAdmins;
using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Tenancy;
using Wolverine;

namespace ControlPlane.Infrastructure;

/// <summary>
/// The <c>catalog</c> schema: the control plane's data above tenants (tenants, users, roles, system admins), and the memberships,
/// invitations and tenant onboardings, which belong to a tenant and are under row level security.
/// </summary>
/// <remarks>Public only because Wolverine's generated code creates it for the handlers (W9); its sets are internal to the module.</remarks>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IMessageContext? messaging = null)
    : TenantDbContext(options, messaging)
{
    public const string Schema = "catalog";

    internal DbSet<Tenant> Tenants => Set<Tenant>();

    internal DbSet<User> Users => Set<User>();

    internal DbSet<Membership> Memberships => Set<Membership>();

    internal DbSet<Role> Roles => Set<Role>();

    internal DbSet<Invitation> Invitations => Set<Invitation>();

    internal DbSet<SystemAdmin> SystemAdmins => Set<SystemAdmin>();

    internal DbSet<TenantOnboarding> TenantOnboardings => Set<TenantOnboarding>();

    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Id).ValueGeneratedNever();
            tenant.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength);
            tenant.Property(t => t.Slug).HasMaxLength(63);
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            tenant.Property(t => t.CancellationReason).HasMaxLength(Tenant.CancellationReasonMaxLength);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.ExternalId).HasMaxLength(255);
            user.HasIndex(u => u.ExternalId).IsUnique();
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.Property(r => r.Id).ValueGeneratedNever();
            role.Property(r => r.Name).HasMaxLength(Role.NameMaxLength);
            role.Property(r => r.BuiltIn).HasConversion<string>().HasMaxLength(20);
            role.Ignore(r => r.Permissions);

            // Built-in roles are shared by every tenant; their permissions come from code, so their rows hold none.
            role.HasData(BuiltInRoles.All.Select(builtIn => new { builtIn.Id, builtIn.Name, builtIn.BuiltIn }));
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.HasKey(m => new { m.TenantId, m.UserId });
            membership.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId);
            membership.HasOne<Role>().WithMany().HasForeignKey(m => m.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.Email).HasMaxLength(Invitation.EmailMaxLength);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            invitation.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId);
            invitation.HasOne<Role>().WithMany().HasForeignKey(i => i.RoleId).OnDelete(DeleteBehavior.Cascade);
            invitation.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Restrict);
            invitation.HasOne<User>().WithMany().HasForeignKey(i => i.AcceptedBy).OnDelete(DeleteBehavior.Restrict);
        });

        // The onboarding saga's record (W6). Wolverine raises its version on each change; as the concurrency token the version is
        // in the update's WHERE clause, so of two messages handled at once the second fails and is tried again (S8).
        modelBuilder.Entity<TenantOnboarding>(onboarding =>
        {
            onboarding.Property(o => o.Id).ValueGeneratedNever();
            onboarding.Property(o => o.State).HasConversion<string>().HasMaxLength(20);
            onboarding.Property(o => o.IdentityProviderInvitationId).HasMaxLength(255);
            onboarding.Property(o => o.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<SystemAdmin>(admin =>
        {
            admin.HasKey(a => a.UserId);
            admin.HasOne<User>().WithOne().HasForeignKey<SystemAdmin>(a => a.UserId);
            admin.HasOne<User>().WithMany().HasForeignKey(a => a.GrantedBy).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
