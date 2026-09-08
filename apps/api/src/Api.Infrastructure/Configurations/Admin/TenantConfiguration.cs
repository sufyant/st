using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations.Admin;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Slug)
            .HasConversion(slug => slug.Value, value => TenantSlug.Create(value))
            .HasColumnName("Slug")
            .HasMaxLength(63)
            .IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique();

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Ignore(t => t.SchemaName);
    }
}
