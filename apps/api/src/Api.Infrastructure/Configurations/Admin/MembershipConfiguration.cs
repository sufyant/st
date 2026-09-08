using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations.Admin;

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

        // Membership/Tenant/User have no navigation properties by design (Phase 2b keeps
        // entities free of cross-aggregate references), so these are shadow foreign keys:
        // they add DB-level referential integrity without changing the domain entities.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);

        // UseXminAsConcurrencyToken() was removed from the Npgsql EF Core provider
        // after v8; this is the documented manual equivalent (same column/behavior
        // contract: maps the Postgres system column `xmin` as a concurrency token).
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
