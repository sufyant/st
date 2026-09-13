using Domain.ControlPlane.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class TenantCredentialConfiguration : IEntityTypeConfiguration<TenantCredential>
{
    public void Configure(EntityTypeBuilder<TenantCredential> builder)
    {
        builder.ToTable("tenant_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.RoleName)
            .HasColumnName("role_name")
            .HasMaxLength(63)
            .HasConversion(roleName => roleName.Value, value => TenantRoleName.Create(value))
            .IsRequired();
        builder.Property(x => x.EncryptedPassword).HasColumnName("encrypted_password").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
