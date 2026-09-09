using Domain.Access.Permissions;
using Domain.Access.Roles;
using Domain.Access.Users;
using Infrastructure.Persistence.Tenants.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<TenantUser> Users => Set<TenantUser>();
    public DbSet<TenantPermission> Permissions => Set<TenantPermission>();
    public DbSet<TenantRole> Roles => Set<TenantRole>();
    public DbSet<TenantRolePermission> RolePermissions => Set<TenantRolePermission>();
    public DbSet<TenantUserRole> UserRoles => Set<TenantUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(
        typeof(TenantDbContext).Assembly,
        type => type.Namespace == typeof(TenantUserConfiguration).Namespace);
}
