using Domain.Authorization;
using Infrastructure.Persistence.Tenants.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(
        typeof(TenantDbContext).Assembly,
        type => type.Namespace == typeof(TenantUserConfiguration).Namespace);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();
        builder.Properties<RoleId>().HaveConversion<RoleIdConverter>();
        builder.Properties<PermissionId>().HaveConversion<PermissionIdConverter>();
    }
}
