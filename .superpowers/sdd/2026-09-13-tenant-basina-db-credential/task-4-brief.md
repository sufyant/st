### Task 4: `TenantCredential`'ı EF'e bağla ve migration'ı yeniden üret

**Files:**
- Create: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/TenantCredentialConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/TypedIdConverters.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs`
- Delete + regenerate: `apps/api/src/Infrastructure/Persistence/ControlPlane/Migrations/*`
- Modify: `apps/api/tests/IntegrationTests/ControlPlaneSchemaMigrationTests.cs`

**Interfaces:**
- Consumes: `TenantCredential`/`TenantCredentialId` (Task 3).
- Produces: `ControlPlaneDbContext.TenantCredentials -> DbSet<TenantCredential>`. Task 7, 9 bunu kullanır. `control.tenant_credentials` tablosu.

- [x] **Step 1: EF konfigürasyonunu yaz**

```csharp
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
```

- [x] **Step 2: Typed id converter'ı ekle**

`TypedIdConverters.cs`'e ekle:
```csharp
public sealed class TenantCredentialIdConverter() : ValueConverter<TenantCredentialId, Guid>(
    id => id.Value,
    value => new TenantCredentialId(value));
```

- [x] **Step 3: `ControlPlaneDbContext`'e bağla**

`ControlPlaneDbContext.cs`'e ekle:
```csharp
public DbSet<TenantCredential> TenantCredentials => Set<TenantCredential>();
```
ve `ConfigureConventions`'a:
```csharp
builder.Properties<TenantCredentialId>().HaveConversion<TenantCredentialIdConverter>();
```

- [x] **Step 4: Migration'ı yeniden üret**

```bash
rm -rf src/Infrastructure/Persistence/ControlPlane/Migrations
dotnet ef migrations add InitialControlPlane \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext \
  --output-dir Persistence/ControlPlane/Migrations
```

- [x] **Step 5: Şema testini güncelle**

`ControlPlaneSchemaMigrationTests.cs`'e `tenant_credentials` tablosunun varlığını doğrulayan bir assertion ekle (mevcut `membershipCommand` desenini tekrarla):
```csharp
await using var credentialsCommand = new NpgsqlCommand(
    "SELECT to_regclass('control.tenant_credentials')::text", connection);
var credentialsResult = await credentialsCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
var credentialsTableName = credentialsResult is DBNull ? null : (string)credentialsResult!;
```
ve assert bölümüne:
```csharp
Assert.Equal("control.tenant_credentials", credentialsTableName);
```

- [x] **Step 6: Testleri çalıştır**

Run: `dotnet test --solution Api.slnx`
Expected: PASS (tüm testler, migration regenerasyonu control plane'e dokunan her testi etkiler)

- [x] **Step 7: Commit**

```bash
git add apps/api/src/Infrastructure/Persistence/ControlPlane apps/api/tests/IntegrationTests/ControlPlaneSchemaMigrationTests.cs
git commit -m "feat(persistence): add tenant_credentials table"
```

---
