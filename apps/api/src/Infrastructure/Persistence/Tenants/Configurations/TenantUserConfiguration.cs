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
        builder.HasMany(x => x.Roles)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "user_roles",
                right => right.HasOne<Role>().WithMany().HasForeignKey("role_id").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<User>().WithMany().HasForeignKey("user_id").OnDelete(DeleteBehavior.Cascade),
                join => join.HasKey("user_id", "role_id"));
        builder.Navigation(x => x.Roles).HasField("roles").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
