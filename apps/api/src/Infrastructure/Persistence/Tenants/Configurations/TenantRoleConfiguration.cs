using Domain.Shared;
using Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Tenants.Configurations;

public sealed class TenantRoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasData(AccessCatalog.Roles.Select(role => new
        {
            role.Id,
            role.Code,
            role.Name,
            role.Description
        }));
        builder.HasMany(x => x.Permissions)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "role_permissions",
                right => right.HasOne<Permission>().WithMany().HasForeignKey("permission_id").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<Role>().WithMany().HasForeignKey("role_id").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.HasKey("role_id", "permission_id");
                    join.HasData(AccessCatalog.Roles.SelectMany(role =>
                        role.PermissionIds.Select(permissionId => new
                        {
                            role_id = role.Id,
                            permission_id = permissionId
                        })));
                });
        builder.Navigation(x => x.Permissions).HasField("permissions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
