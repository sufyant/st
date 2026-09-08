using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();

        // UseXminAsConcurrencyToken() was removed from the Npgsql EF Core provider
        // after v8; this is the documented manual equivalent (same column/behavior
        // contract: maps the Postgres system column `xmin` as a concurrency token).
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
