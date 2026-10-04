using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.SystemAdmins;
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

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<SystemAdmin> SystemAdmins => Set<SystemAdmin>();

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

        modelBuilder.Entity<Role>(role =>
        {
            role.Property(r => r.Id).ValueGeneratedNever();
            role.Property(r => r.Name).HasMaxLength(Role.NameMaxLength);
            role.Property(r => r.BuiltIn).HasConversion<string>().HasMaxLength(20);
            role.Property<string[]>("_permissions").HasColumnName("permissions");
            role.Ignore(r => r.Permissions);
            role.Ignore(r => r.IsOwner);
            role.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            role.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Cascade);
            role.ToTable(table => table.HasCheckConstraint("ck_roles_built_in_xor_tenant", "(built_in IS NULL) <> (tenant_id IS NULL)"));

            // Built-in roles are shared by every tenant; their permissions come from code, so their rows hold none (0030).
            role.HasData(BuiltInRoles.All.Select(builtIn => new
            {
                builtIn.Id,
                TenantId = (Guid?)null,
                builtIn.Name,
                builtIn.BuiltIn,
                _permissions = Array.Empty<string>(),
            }));
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.HasKey(m => new { m.TenantId, m.UserId });
            membership.Ignore(m => m.IsOwner);
            membership.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId);

            // A role in use cannot be deleted (0030).
            membership.HasOne<Role>().WithMany().HasForeignKey(m => m.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.Email).HasMaxLength(Invitation.EmailMaxLength);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            invitation.HasIndex(i => i.TenantId);
            invitation.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId);

            // A role with pending invitations cannot be deleted, which the handler checks; the history of used invitations goes
            // with it.
            invitation.HasOne<Role>().WithMany().HasForeignKey(i => i.RoleId).OnDelete(DeleteBehavior.Cascade);
            invitation.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Restrict);
            invitation.HasOne<User>().WithMany().HasForeignKey(i => i.AcceptedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemAdmin>(admin =>
        {
            admin.HasKey(a => a.UserId);
            admin.Property(a => a.Role).HasConversion<string>().HasMaxLength(20);
            admin.HasOne<User>().WithOne().HasForeignKey<SystemAdmin>(a => a.UserId);
            admin.HasOne<User>().WithMany().HasForeignKey(a => a.GrantedBy).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
