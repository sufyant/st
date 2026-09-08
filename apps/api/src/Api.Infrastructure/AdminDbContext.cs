using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public const string SchemaName = "admin";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Attach the audit interceptor here so every AdminDbContext instance gets it
        // automatically, regardless of how its DbContextOptions were built - no
        // per-call-site registration to forget.
        optionsBuilder.AddInterceptors(new AuditableSaveChangesInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Ignore<DomainEvent>();

        // Scope the scan to the Admin configurations namespace only, so a future
        // IEntityTypeConfiguration<T> added elsewhere in this assembly (e.g. for
        // TenantDbContext) isn't silently picked up here too.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AdminDbContext).Assembly,
            type => type.Namespace != null && type.Namespace.Contains(".Configurations.Admin"));
    }
}
