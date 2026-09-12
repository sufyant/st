using Domain.Shared;
using Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Tenants.Configurations;

public sealed class TenantUserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ExternalUserId).HasColumnName("external_user_id").HasMaxLength(255).HasConversion(x => x.Value, x => ExternalUserId.Create(x));
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value))
            .IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.ExternalUserId).IsUnique();
        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey("role_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
