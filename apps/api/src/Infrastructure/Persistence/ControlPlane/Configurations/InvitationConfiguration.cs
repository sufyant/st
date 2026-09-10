using Domain.Access;
using Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value))
            .IsRequired();
        builder.Property(x => x.RoleCode).HasColumnName("role_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.InvitedByExternalUserId)
            .HasColumnName("invited_by_external_user_id")
            .HasMaxLength(255)
            .HasConversion(userId => userId.Value, value => ExternalUserId.Create(value))
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.AcceptedByExternalUserId)
            .HasColumnName("accepted_by_external_user_id")
            .HasMaxLength(255)
            .HasConversion(
                userId => userId!.Value,
                value => ExternalUserId.Create(value));
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Email })
            .IsUnique()
            .HasFilter("status = 'Pending'");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
