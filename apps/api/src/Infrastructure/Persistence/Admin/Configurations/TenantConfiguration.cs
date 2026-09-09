using Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Admin.Configurations;

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
        builder.HasIndex(x => x.Alias).IsUnique();
        builder.Ignore(x => x.DatabaseName);
    }
}
