using Domain.ControlPlane.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Alias)
            .HasColumnName("alias")
            .HasMaxLength(63)
            .HasConversion(alias => alias.Value, value => TenantAlias.Create(value))
            .IsRequired();
        builder.Property(x => x.DatabaseName)
            .HasColumnName("database_name")
            .HasMaxLength(63)
            .HasConversion(name => name.Value, value => TenantDatabaseName.Create(value))
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ProvisioningStep)
            .HasColumnName("provisioning_step")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.ProvisioningError).HasColumnName("provisioning_error");
        builder.HasIndex(x => x.Alias).IsUnique();
        builder.HasIndex(x => x.DatabaseName).IsUnique();
    }
}
