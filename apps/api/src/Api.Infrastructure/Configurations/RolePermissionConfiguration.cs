using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.Role).HasMaxLength(100).IsRequired();
        builder.Property(rp => rp.Permission).HasMaxLength(100).IsRequired();

        builder.HasIndex(rp => new { rp.Role, rp.Permission }).IsUnique();
    }
}
