# Domain Katmanı Yapısal Düzenleme — Uygulama Planı (Faz 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** [Spec 6](../specs/2026-09-11-domain-katmani-design.md)'nın davranış değiştirmeyen yapısal yarısını uygulamak: klasör düzeni, isimlendirme, `Entity<TId>` tabanı, tipli kimlikler, kapalı statü geçişleri, ilişki sınıflarının kalkması, denetim damgalarının interceptor'e taşınması.

**Architecture:** `src/Domain` iki üst klasöre ayrılır: `ControlPlane` (control_plane veritabanı) ve `Authorization` (tenant veritabanı), ortak değer nesneleri `Shared` altında. Entity'ler `Entity<TId>` tabanından türer, kimlikler `readonly record struct` olur. Rol atamaları EF skip navigation'a döner ve ilişki sınıfları domainden çıkar. Zaman damgaları bir EF `SaveChangesInterceptor` tarafından yazılır, domain tiplerinden zaman parametreleri kalkar.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core (Npgsql), PostgreSQL, xUnit v3 (Microsoft.Testing.Platform), Testcontainers.

## Global Constraints

- Hedef `net10.0`. `Domain` projesi hiçbir pakete ve hiçbir projeye referans vermez.
- Faz 1'de **hiçbir domain event yazılmaz.** `AggregateRoot<TId>`, `IDomainEvent` ve event dağıtımı Faz 2'ye aittir (Spec 6, karar 4: dağıtım olmadan `Raise` yazılmaz). Faz 1'de bütün entity'ler `Entity<TId>`'den türer.
- Faz 1'de davet akışına, token'a, Clerk'e ve `role_code`'a **dokunulmaz.** Onlar Faz 2'dir.
- Migration biriktirilmez. Şemayı değiştiren her görev, ilgili context'in `Migrations` klasörünü silip tek bir başlangıç migration'ı yeniden üretir (Spec 6, karar 16). Migration adları `InitialControlPlane` ve `InitialTenantAccess`.
- Her görev şu ikisi yeşilken biter: `dotnet build Api.slnx` ve `dotnet test --solution Api.slnx`.
- Kodu tekrar eden yorum yazılmaz. Yorum yalnızca aşikâr olmayan kısıtı açıklar. Testlerde `// Arrange`, `// Act`, `// Assert` etiketleri zorunludur.
- Bir konfigürasyon anahtarı eklenir veya adı değişirse `src/Api/appsettings.Development.example.json` aynı değişiklikte güncellenir.
- Bütün komutlar `apps/api` dizininden çalıştırılır.
- Entegrasyon testleri Docker ister: `docker compose up -d`.

### Sık kullanılan komutlar

```bash
dotnet build Api.slnx
dotnet test --solution Api.slnx
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*TestAdi*'
dotnet test --project tests/IntegrationTests/IntegrationTests.csproj
dotnet tool restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context TenantDbContext
```

---

### Task 1: Domain klasör ve isim taşıması

Davranış değişmez. Bu görevin testi mevcut test takımıdır: taşımadan önce yeşil, taşımadan sonra yeşil.

**Files:**
- Move: `src/Domain/Tenants/*.cs` → `src/Domain/ControlPlane/Tenants/`
- Move: `src/Domain/Access/Membership.cs` → `src/Domain/ControlPlane/Memberships/Membership.cs`
- Move: `src/Domain/Access/Invitation.cs` → `src/Domain/ControlPlane/Invitations/Invitation.cs`
- Move: `src/Domain/Access/PlatformAdmin.cs` → `src/Domain/ControlPlane/Administration/PlatformAdmin.cs`
- Move: `src/Domain/Access/EmailAddress.cs` → `src/Domain/Shared/EmailAddress.cs`
- Move: `src/Domain/Access/ExternalUserId.cs` → `src/Domain/Shared/ExternalUserId.cs`
- Move+Rename: `src/Domain/Access/SystemAccessCatalog.cs` → `src/Domain/Authorization/AccessCatalog.cs`
- Move+Rename: `src/Domain/Access/Users/TenantUser.cs` → `src/Domain/Authorization/User.cs`
- Move+Rename: `src/Domain/Access/Users/TenantUserRole.cs` → `src/Domain/Authorization/UserRole.cs`
- Move+Rename: `src/Domain/Access/Users/OwnerRoster.cs` → `src/Domain/Authorization/OwnerRoster.cs`
- Move+Rename: `src/Domain/Access/Roles/TenantRole.cs` → `src/Domain/Authorization/Role.cs`
- Move+Rename: `src/Domain/Access/Roles/TenantRolePermission.cs` → `src/Domain/Authorization/RolePermission.cs`
- Move+Rename: `src/Domain/Access/Permissions/TenantPermission.cs` → `src/Domain/Authorization/Permission.cs`
- Move: `tests/UnitTests/Access/*` → `tests/UnitTests/Authorization/` ve `tests/UnitTests/ControlPlane/`
- Modify: `src/Domain/Authorization/` içine tek satırlık bir `README` yerine, `AccessCatalog.cs` başına klasörün hangi veritabanına ait olduğunu söyleyen tek satırlık yorum
- Modify: `Domain`, `Infrastructure`, `Application`, `Api`, `ControlPlane` ve üç test projesindeki bütün `using` satırları ve tip adları

**Interfaces:**
- Consumes: yok, ilk görev.
- Produces: `Domain.Shared.EmailAddress`, `Domain.Shared.ExternalUserId`, `Domain.ControlPlane.Tenants.{Tenant, TenantAlias, TenantDatabaseName, TenantStatus, TenantProvisioningStep}`, `Domain.ControlPlane.Memberships.Membership`, `Domain.ControlPlane.Invitations.{Invitation, InvitationStatus}`, `Domain.ControlPlane.Administration.PlatformAdmin`, `Domain.Authorization.{User, UserStatus, UserRole, Role, RolePermission, Permission, OwnerRoster, AccessCatalog, RoleDefinition, PermissionDefinition}`.

- [ ] **Step 1: Başlangıç durumunu kanıtla**

```bash
docker compose up -d
dotnet test --solution Api.slnx
```

Beklenen: PASS. Kaç test geçtiğini not al; Step 6'da aynı sayı beklenecek.

- [ ] **Step 2: Dosyaları taşı**

```bash
mkdir -p src/Domain/Shared src/Domain/ControlPlane/Tenants src/Domain/ControlPlane/Memberships \
         src/Domain/ControlPlane/Invitations src/Domain/ControlPlane/Administration src/Domain/Authorization

git mv src/Domain/Tenants/Tenant.cs                    src/Domain/ControlPlane/Tenants/Tenant.cs
git mv src/Domain/Tenants/TenantAlias.cs               src/Domain/ControlPlane/Tenants/TenantAlias.cs
git mv src/Domain/Tenants/TenantDatabaseName.cs        src/Domain/ControlPlane/Tenants/TenantDatabaseName.cs
git mv src/Domain/Tenants/TenantStatus.cs              src/Domain/ControlPlane/Tenants/TenantStatus.cs
git mv src/Domain/Tenants/TenantProvisioningStep.cs    src/Domain/ControlPlane/Tenants/TenantProvisioningStep.cs
git mv src/Domain/Access/Membership.cs                 src/Domain/ControlPlane/Memberships/Membership.cs
git mv src/Domain/Access/Invitation.cs                 src/Domain/ControlPlane/Invitations/Invitation.cs
git mv src/Domain/Access/PlatformAdmin.cs              src/Domain/ControlPlane/Administration/PlatformAdmin.cs
git mv src/Domain/Access/EmailAddress.cs               src/Domain/Shared/EmailAddress.cs
git mv src/Domain/Access/ExternalUserId.cs             src/Domain/Shared/ExternalUserId.cs
git mv src/Domain/Access/SystemAccessCatalog.cs        src/Domain/Authorization/AccessCatalog.cs
git mv src/Domain/Access/Users/TenantUser.cs           src/Domain/Authorization/User.cs
git mv src/Domain/Access/Users/TenantUserRole.cs       src/Domain/Authorization/UserRole.cs
git mv src/Domain/Access/Users/OwnerRoster.cs          src/Domain/Authorization/OwnerRoster.cs
git mv src/Domain/Access/Roles/TenantRole.cs           src/Domain/Authorization/Role.cs
git mv src/Domain/Access/Roles/TenantRolePermission.cs src/Domain/Authorization/RolePermission.cs
git mv src/Domain/Access/Permissions/TenantPermission.cs src/Domain/Authorization/Permission.cs
rmdir src/Domain/Tenants src/Domain/Access/Users src/Domain/Access/Roles src/Domain/Access/Permissions src/Domain/Access
```

- [ ] **Step 3: Namespace'leri ve tip adlarını değiştir**

Sırası önemlidir: uzun adlar kısa adlardan önce değişmeli, yoksa `TenantUserRole` önce `TenantUser` kuralına yakalanır.

```bash
FILES=$(git ls-files '*.cs' | grep -v '/obj/' | grep -v '/bin/' | grep -v '/Migrations/')

# namespace ve using yolları
perl -pi -e 's/\bDomain\.Access\.Users\b/Domain.Authorization/g;
             s/\bDomain\.Access\.Roles\b/Domain.Authorization/g;
             s/\bDomain\.Access\.Permissions\b/Domain.Authorization/g;
             s/\bDomain\.Tenants\b/Domain.ControlPlane.Tenants/g;
             s/\bDomain\.Access\b/Domain.Shared/g;' $FILES

# tip adlari, uzundan kisaya
perl -pi -e 's/\bSystemPermissionDefinition\b/PermissionDefinition/g;
             s/\bSystemRoleDefinition\b/RoleDefinition/g;
             s/\bSystemAccessCatalog\b/AccessCatalog/g;
             s/\bTenantRolePermission\b/RolePermission/g;
             s/\bTenantUserStatus\b/UserStatus/g;
             s/\bTenantUserRole\b/UserRole/g;
             s/\bTenantPermission\b(?!s)/Permission/g;
             s/\bTenantUser\b/User/g;
             s/\bTenantRole\b/Role/g;' $FILES
```

`TenantPermissions` (Application'daki izin kodu sabitleri sınıfı) korunmalıdır; yukarıdaki `(?!s)` bakışı onu dışarıda bırakır. Değişiklikten sonra doğrula:

```bash
grep -rn '\bTenantPermissions\b' --include='*.cs' src | head -5
```

Beklenen: `src/Application/Abstractions/TenantPermissions.cs` hâlâ bu adla duruyor.

- [ ] **Step 4: Taşınan dosyaların namespace satırlarını elle düzelt**

`perl` yalnızca tam nitelikli adları değiştirdi; taşınan dosyaların kendi `namespace` satırları elden geçirilmeli.

| Dosya | `namespace` |
|---|---|
| `src/Domain/Shared/EmailAddress.cs`, `ExternalUserId.cs` | `Domain.Shared` |
| `src/Domain/ControlPlane/Tenants/*.cs` | `Domain.ControlPlane.Tenants` |
| `src/Domain/ControlPlane/Memberships/Membership.cs` | `Domain.ControlPlane.Memberships` |
| `src/Domain/ControlPlane/Invitations/Invitation.cs` | `Domain.ControlPlane.Invitations` |
| `src/Domain/ControlPlane/Administration/PlatformAdmin.cs` | `Domain.ControlPlane.Administration` |
| `src/Domain/Authorization/*.cs` | `Domain.Authorization` |

`AccessCatalog.cs` dosyasının başına, dosyanın hangi veritabanına ait olduğunu söyleyen tek satır eklenir:

```csharp
// Bu klasördeki tipler tenant veritabanında yaşar; control plane onları hiç görmez.
namespace Domain.Authorization;
```

- [ ] **Step 5: Test dosyalarını taşı**

```bash
mkdir -p tests/UnitTests/Authorization tests/UnitTests/ControlPlane
git mv tests/UnitTests/Access/TenantUserTests.cs       tests/UnitTests/Authorization/UserTests.cs
git mv tests/UnitTests/Access/OwnerRosterTests.cs      tests/UnitTests/Authorization/OwnerRosterTests.cs
git mv tests/UnitTests/Access/PermissionCatalogTests.cs tests/UnitTests/Authorization/PermissionCatalogTests.cs
git mv tests/UnitTests/Access/SystemAccessCatalogTests.cs tests/UnitTests/Authorization/AccessCatalogTests.cs
git mv tests/UnitTests/Access/InvitationTests.cs       tests/UnitTests/ControlPlane/InvitationTests.cs
git mv tests/UnitTests/Access/MembershipTests.cs       tests/UnitTests/ControlPlane/MembershipTests.cs
git mv tests/UnitTests/Access/PlatformAdminTests.cs    tests/UnitTests/ControlPlane/PlatformAdminTests.cs
git mv tests/UnitTests/Access/EmailAddressTests.cs     tests/UnitTests/Shared/EmailAddressTests.cs 2>/dev/null \
  || (mkdir -p tests/UnitTests/Shared && git mv tests/UnitTests/Access/EmailAddressTests.cs tests/UnitTests/Shared/EmailAddressTests.cs)
git mv tests/UnitTests/Tenants tests/UnitTests/ControlPlaneTenants
rmdir tests/UnitTests/Access
```

Test sınıflarının kendi `namespace` satırları ve sınıf adları da klasörle uyumlu hale getirilir: `UnitTests.Authorization`, `UnitTests.ControlPlane`, `UnitTests.Shared`, `UnitTests.ControlPlaneTenants`. `TenantUserTests` sınıfı `UserTests`, `SystemAccessCatalogTests` sınıfı `AccessCatalogTests` olur.

- [ ] **Step 6: Derle ve bütün testleri çalıştır**

```bash
dotnet build Api.slnx
dotnet test --solution Api.slnx
```

Beklenen: PASS, Step 1'deki test sayısıyla aynı.

Derleyici `Authorization` veya `ControlPlane` namespace'i için belirsizlik hatası verirse (`Api.Authorization`, `ControlPlane` ve `Infrastructure.Persistence.ControlPlane` ile çakışma), ilgili dosyada açık `using` yazılır; `global using` eklenmez.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(domain): split the domain by owning database

The Access folder held entities from both databases, which hid the isolation
boundary this system is built around. Control plane types now live under
ControlPlane, tenant database types under Authorization, and the two value
objects both sides share under Shared. The Tenant prefix drops from type names
because the namespace already says it."
```

---

### Task 2: `Entity<TId>` tabanı

**Files:**
- Create: `src/Domain/Shared/Entity.cs`
- Create: `tests/UnitTests/Shared/EntityTests.cs`

**Interfaces:**
- Consumes: Task 1'in namespace düzeni.
- Produces: `public abstract class Domain.Shared.Entity<TId> where TId : struct`; `TId Id { get; protected set; }`; kimlik ve tip üzerinden eşitlik; `==` ve `!=` operatörleri.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/Shared/EntityTests.cs`:

```csharp
using Domain.Shared;
using Xunit;

namespace UnitTests.Shared;

public sealed class EntityTests
{
    private sealed class Cat : Entity<Guid>
    {
        public Cat(Guid id) => Id = id;
    }

    private sealed class Dog : Entity<Guid>
    {
        public Dog(Guid id) => Id = id;
    }

    [Fact]
    public void TwoInstancesWithTheSameIdentityAreEqual()
    {
        // Arrange
        var id = Guid.CreateVersion7();

        // Act
        var left = new Cat(id);
        var right = new Cat(id);

        // Assert
        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void TheSameIdentityInADifferentTypeIsNotEqual()
    {
        // Arrange
        var id = Guid.CreateVersion7();

        // Act
        var cat = new Cat(id);
        var dog = new Dog(id);

        // Assert
        Assert.NotEqual<object>(cat, dog);
    }

    [Fact]
    public void ComparingWithNullIsSafe()
    {
        // Arrange
        Cat? absent = null;

        // Act
        var cat = new Cat(Guid.CreateVersion7());

        // Assert
        Assert.False(cat == absent);
        Assert.True(cat != absent);
        Assert.True(absent == null);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*EntityTests*'
```

Beklenen: derleme hatası, `Entity<TId>` bulunamıyor.

- [ ] **Step 3: `Entity<TId>` yaz**

`src/Domain/Shared/Entity.cs`:

```csharp
namespace Domain.Shared;

public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && other.Id.Equals(Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
```

- [ ] **Step 4: Testin geçtiğini gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*EntityTests*'
```

Beklenen: PASS, 3 test.

- [ ] **Step 5: Commit**

```bash
git add src/Domain/Shared/Entity.cs tests/UnitTests/Shared/EntityTests.cs
git commit -m "feat(domain): add the entity base type

Identity equality is the one thing every entity shares. The equality operators
are overridden alongside Equals so a comparison never falls back to reference
equality without anyone noticing."
```

---

### Task 3: Control plane tipli kimlikleri

**Files:**
- Modify: `src/Domain/ControlPlane/Tenants/Tenant.cs` (`TenantId` eklenir, `Tenant : Entity<TenantId>`)
- Modify: `src/Domain/ControlPlane/Memberships/Membership.cs` (`MembershipId`)
- Modify: `src/Domain/ControlPlane/Invitations/Invitation.cs` (`InvitationId`)
- Modify: `src/Domain/ControlPlane/Administration/PlatformAdmin.cs` (`PlatformAdminId`)
- Modify: `src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs` (`ConfigureConventions`)
- Modify: `src/Infrastructure/Persistence/ControlPlane/Configurations/*.cs`
- Modify: `src/Infrastructure/Tenants/TenantResolver.cs`, `src/Infrastructure/Provisioning/TenantProvisioner.cs`, `src/Infrastructure/Persistence/Tenants/TenantMigrationRunner.cs`
- Modify: `src/Application/Abstractions/TenantContext.cs`, `src/Application/Features/**`
- Modify: `src/ControlPlane/TenantEndpoints.cs`, `src/ControlPlane/Authorization/PlatformAdminHandler.cs`
- Modify: `src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: ilgili testler

**Interfaces:**
- Consumes: `Domain.Shared.Entity<TId>`.
- Produces:
  - `readonly record struct TenantId(Guid Value)` — `Domain.ControlPlane.Tenants`
  - `readonly record struct MembershipId(Guid Value)` — `Domain.ControlPlane.Memberships`
  - `readonly record struct InvitationId(Guid Value)` — `Domain.ControlPlane.Invitations`
  - `readonly record struct PlatformAdminId(Guid Value)` — `Domain.ControlPlane.Administration`
  - Her biri: `static X New()` ve `static X From(Guid value)`.
  - `Tenant.Create(TenantAlias alias, DateTimeOffset createdAt)` — kimlik artık dışarıdan verilmez, `TenantId.New()` ile üretilir.
  - `Membership.Create(TenantId tenantId, ExternalUserId externalUserId)`
  - `Invitation.Create(TenantId tenantId, EmailAddress email, string roleCode, string tokenHash, ExternalUserId invitedBy, DateTimeOffset createdAt, TimeSpan lifetime)`
  - `PlatformAdmin.Create(ExternalUserId externalUserId, DateTimeOffset createdAt)`
  - `TenantContext.TenantId` tipi `Guid` yerine `TenantId`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/ControlPlaneTenants/TenantIdTests.cs`:

```csharp
using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        var act = () => TenantId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void From_KeepsTheValue()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var id = TenantId.From(value);

        // Assert
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void New_ProducesDistinctSortableIdentifiers()
    {
        // Arrange & Act
        var first = TenantId.New();
        var second = TenantId.New();

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(Guid.Empty, first.Value);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*TenantIdTests*'
```

Beklenen: derleme hatası, `TenantId` bulunamıyor.

- [ ] **Step 3: Dört kimliği tanımla ve entity'leri türet**

`src/Domain/ControlPlane/Tenants/Tenant.cs` başına:

```csharp
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant ID cannot be empty.", nameof(value))
        : new TenantId(value);
}
```

`Tenant` sınıfı `Entity<TenantId>`'den türer, kendi `Id` özelliğini siler ve `Create` şu hale gelir:

```csharp
public static Tenant Create(TenantAlias alias, DateTimeOffset createdAt)
{
    ArgumentNullException.ThrowIfNull(alias);

    var id = TenantId.New();

    return new Tenant
    {
        Id = id,
        Alias = alias,
        DatabaseName = TenantDatabaseName.ForTenant(id),
        Status = TenantStatus.Provisioning,
        ProvisioningStep = TenantProvisioningStep.CreatingDatabase,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };
}
```

`TenantDatabaseName.ForTenant` imzası `TenantId` alır ve içindeki boş `Guid` kontrolü silinir, çünkü `TenantId` onu zaten garanti eder:

```csharp
public static TenantDatabaseName ForTenant(TenantId tenantId) =>
    new($"tenant_{tenantId.Value:N}");
```

Aynı desen `MembershipId`, `InvitationId`, `PlatformAdminId` için tekrarlanır; her biri kendi entity dosyasının başında durur, hata mesajında kendi adını kullanır (`"Membership ID cannot be empty."` gibi). Üç entity de `Entity<XId>`'den türer, kendi `Id` özelliklerini siler ve factory'lerinden hem `id` parametresi hem boş `Guid` kontrolü kalkar.

- [ ] **Step 4: EF dönüştürücülerini tek yerden tanımla**

`src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs` içine:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
{
    builder.Properties<TenantId>().HaveConversion<TenantIdConverter>();
    builder.Properties<MembershipId>().HaveConversion<MembershipIdConverter>();
    builder.Properties<InvitationId>().HaveConversion<InvitationIdConverter>();
    builder.Properties<PlatformAdminId>().HaveConversion<PlatformAdminIdConverter>();
}
```

`src/Infrastructure/Persistence/ControlPlane/TypedIdConverters.cs`:

```csharp
using Domain.ControlPlane.Administration;
using Domain.ControlPlane.Invitations;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.ControlPlane;

public sealed class TenantIdConverter() : ValueConverter<TenantId, Guid>(
    id => id.Value,
    value => new TenantId(value));

public sealed class MembershipIdConverter() : ValueConverter<MembershipId, Guid>(
    id => id.Value,
    value => new MembershipId(value));

public sealed class InvitationIdConverter() : ValueConverter<InvitationId, Guid>(
    id => id.Value,
    value => new InvitationId(value));

public sealed class PlatformAdminIdConverter() : ValueConverter<PlatformAdminId, Guid>(
    id => id.Value,
    value => new PlatformAdminId(value));
```

Materyalizasyon `From` değil doğrudan kurucuyu kullanır; veritabanındaki değer zaten doğrulanmış kabul edilir ve boş kimlik için atmak okuma yolunu patlatırdı.

- [ ] **Step 5: Çağrı yerlerini ve testleri güncelle**

`TenantContext.TenantId` tipi `TenantId` olur. `TenantResolver`, `TenantProvisioner`, `TenantMigrationRunner`, `TenantEndpoints`, `PlatformAdminHandler`, `TenantAccessMiddleware` ve `Application/Features` altındaki bütün handler'lar buna göre düzeltilir. HTTP yüzeyinde rota parametreleri `Guid` kalır ve sınırda `TenantId.From(id)` ile çevrilir; DTO'lara tipli kimlik sızmaz.

- [ ] **Step 6: Şemanın değişmediğini kanıtla**

```bash
dotnet build Api.slnx
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext
```

Beklenen: "No changes have been made to the model since the last migration." Tipli kimlik aynı `uuid` kolonuna yazar.

Değişiklik bildirirse `src/Infrastructure/Persistence/ControlPlane/Migrations` klasörü silinir ve tek migration yeniden üretilir:

```bash
rm -rf src/Infrastructure/Persistence/ControlPlane/Migrations
dotnet tool run dotnet-ef migrations add InitialControlPlane --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
```

- [ ] **Step 7: Bütün testleri çalıştır**

```bash
dotnet test --solution Api.slnx
```

Beklenen: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(domain): give control plane entities typed identifiers

Membership.Create took an id and a tenant id side by side, so swapping them
compiled. The wrappers make that a compile error and they take the empty guid
check with them: every factory used to repeat it, now From holds it alone."
```

---

### Task 4: Authorization tipli kimlikleri

**Files:**
- Modify: `src/Domain/Authorization/User.cs` (`UserId`), `Role.cs` (`RoleId`), `Permission.cs` (`PermissionId`), `UserRole.cs`, `RolePermission.cs`, `OwnerRoster.cs`
- Create: `src/Infrastructure/Persistence/Tenants/TypedIdConverters.cs`
- Modify: `src/Infrastructure/Persistence/Tenants/TenantDbContext.cs` ve `Configurations/*.cs`
- Modify: `src/Api/Tenants/TenantAccessMiddleware.cs`, `src/Application/Features/Members/*`, `src/Application/Features/Me/AcceptInvitation.cs`, `src/Application/Features/Provisioning/TenantProvisioningHandler.cs`
- Modify: ilgili testler

**Interfaces:**
- Consumes: Task 2 ve Task 3'ün desenleri.
- Produces:
  - `readonly record struct UserId(Guid Value)`, `RoleId(Guid Value)`, `PermissionId(Guid Value)` — hepsi `Domain.Authorization`, hepsinde `New()` ve `From(Guid)`.
  - `User.Create(ExternalUserId externalUserId, UserStatus status)` — kimlik içeride üretilir.
  - `UserRole.Create(UserId userId, RoleId roleId)`
  - `OwnerRoster.Of(IReadOnlyCollection<UserId> activeOwnerIds)` ve `IsLastOwner(UserId userId)`
  - `RoleDefinition.Id` tipi `RoleId`, `PermissionDefinition.Id` tipi `PermissionId`, `RoleDefinition.PermissionIds` tipi `IReadOnlySet<PermissionId>`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/Authorization/UserIdTests.cs`:

```csharp
using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class UserIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        var act = () => UserId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void New_ProducesDistinctIdentifiers()
    {
        // Arrange & Act
        var first = UserId.New();
        var second = UserId.New();

        // Assert
        Assert.NotEqual(first, second);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*UserIdTests*'
```

Beklenen: derleme hatası.

- [ ] **Step 3: Üç kimliği tanımla ve tipleri güncelle**

`UserId`, `RoleId`, `PermissionId` Task 3'teki desenle yazılır; her biri kendi entity dosyasının başında durur. `User` `Entity<UserId>`'den türer. `Role` ve `Permission` **hiçbir tabandan türemez** (Spec 6, karar 9) ama `Id` özelliklerinin tipi `RoleId` ve `PermissionId` olur. `AccessCatalog` içindeki sabit GUID'ler `RoleId.From(Guid.Parse("..."))` ve `PermissionId.From(Guid.Parse("..."))` ile sarılır.

- [ ] **Step 4: EF dönüştürücülerini ekle**

`src/Infrastructure/Persistence/Tenants/TypedIdConverters.cs`:

```csharp
using Domain.Authorization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.Tenants;

public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(
    id => id.Value,
    value => new UserId(value));

public sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(
    id => id.Value,
    value => new RoleId(value));

public sealed class PermissionIdConverter() : ValueConverter<PermissionId, Guid>(
    id => id.Value,
    value => new PermissionId(value));
```

`TenantDbContext` içine:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
{
    builder.Properties<UserId>().HaveConversion<UserIdConverter>();
    builder.Properties<RoleId>().HaveConversion<RoleIdConverter>();
    builder.Properties<PermissionId>().HaveConversion<PermissionIdConverter>();
}
```

`HasData` tohumları anonim nesnelerde tipli kimlikleri kullanır; EF dönüştürücüyü tohum verisine de uygular.

- [ ] **Step 5: Şemanın değişmediğini kanıtla**

```bash
dotnet build Api.slnx
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context TenantDbContext
```

Beklenen: değişiklik yok. Değişiklik bildirirse:

```bash
rm -rf src/Infrastructure/Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations
```

- [ ] **Step 6: Bütün testleri çalıştır**

```bash
dotnet test --solution Api.slnx
```

Beklenen: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(domain): give authorization entities typed identifiers

UserRole.Create took two bare guids in a row. Role and permission keep their
seeded identifiers but now carry them in their own types, so a role id can no
longer stand in for a permission id."
```

---

### Task 5: `ExternalUserId` record olur, `EmailAddress.TryCreate` düzelir

**Files:**
- Modify: `src/Domain/Shared/ExternalUserId.cs`
- Modify: `src/Domain/Shared/EmailAddress.cs`
- Modify: `src/Application/Abstractions/ICurrentUser.cs`, `src/Api/Http/CurrentUser.cs`
- Create: `tests/UnitTests/Shared/ExternalUserIdTests.cs`

**Interfaces:**
- Consumes: yok.
- Produces: `sealed record ExternalUserId` — yapısal eşitlik, `==` bedava; `Create(string)` aynı kalır. `EmailAddress.TryCreate(string? value, [MaybeNullWhen(false)] out EmailAddress email)`. `ICurrentUser.TryGetEmail([MaybeNullWhen(false)] out EmailAddress email)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/Shared/ExternalUserIdTests.cs`:

```csharp
using Domain.Shared;
using Xunit;

namespace UnitTests.Shared;

public sealed class ExternalUserIdTests
{
    [Fact]
    public void TheEqualityOperatorComparesTheValue()
    {
        // Arrange
        var left = ExternalUserId.Create("user_2abc123");

        // Act
        var right = ExternalUserId.Create("user_2abc123");

        // Assert
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void DifferentValuesAreNotEqual()
    {
        // Arrange
        var left = ExternalUserId.Create("user_2abc123");

        // Act
        var right = ExternalUserId.Create("user_2def456");

        // Assert
        Assert.True(left != right);
    }

    [Fact]
    public void Create_ForABlankValue_Throws()
    {
        // Arrange
        var value = "   ";

        // Act
        var act = () => ExternalUserId.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
```

İlk test bugünkü sınıf hâliyle **başarısız olur**, çünkü `==` referans eşitliğine düşer.

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*ExternalUserIdTests*'
```

Beklenen: `TheEqualityOperatorComparesTheValue` FAIL.

- [ ] **Step 3: Record'a çevir**

`src/Domain/Shared/ExternalUserId.cs`:

```csharp
namespace Domain.Shared;

public sealed record ExternalUserId
{
    public string Value { get; }

    private ExternalUserId(string value)
    {
        Value = value;
    }

    public static ExternalUserId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("External user ID cannot be blank.", nameof(value));
        }

        return new ExternalUserId(value);
    }
}
```

- [ ] **Step 4: `TryCreate` imzalarını düzelt**

`src/Domain/Shared/EmailAddress.cs` içinde `using System.Diagnostics.CodeAnalysis;` eklenir ve imza şu olur:

```csharp
public static bool TryCreate(string? value, [MaybeNullWhen(false)] out EmailAddress email)
```

Gövdedeki iki `email = null!;` satırı `email = null;` olur. Aynı düzeltme `ICurrentUser.TryGetEmail` ve `Api.Http.CurrentUser.TryGetEmail` için tekrarlanır.

- [ ] **Step 5: Testlerin geçtiğini gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*ExternalUserIdTests*'
dotnet test --solution Api.slnx
```

Beklenen: ikisi de PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(domain): make the external user id compare by value

It was the one value object written as a class, and it never overrode the
equality operator. Every comparison today sits inside an EF expression tree and
is translated to SQL, so nothing is broken yet; the first in-memory comparison
would have silently fallen back to reference equality."
```

---

### Task 6: `Tenant` statü geçişleri kapanır

**Files:**
- Modify: `src/Domain/ControlPlane/Tenants/Tenant.cs`
- Modify: `tests/UnitTests/ControlPlaneTenants/TenantTests.cs`
- Modify: `tests/IntegrationTests/TenantAccessEndpointTests.cs`, `TenantMigrationRunnerTests.cs`, `TenantSurfaceFixture.cs`

**Interfaces:**
- Consumes: Task 3'ün `TenantId`'si.
- Produces: `ChangeStatus` silinir. Yerine: `CompleteProvisioning(DateTimeOffset)`, `Suspend(DateTimeOffset)`, `Resume(DateTimeOffset)`, `BeginDeprovisioning(DateTimeOffset)`, `MarkDeleted(DateTimeOffset)`. Her biri kaynak durumu uymazsa `InvalidOperationException` atar.

- [ ] **Step 1: Başarısız testleri yaz**

`tests/UnitTests/ControlPlaneTenants/TenantTests.cs` içine ekle:

```csharp
[Fact]
public void Suspend_FromProvisioning_Throws()
{
    // Arrange
    var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

    // Act
    var act = () => tenant.Suspend(CreatedAt.AddMinutes(1));

    // Assert
    Assert.Throws<InvalidOperationException>(act);
}

[Fact]
public void Resume_FromActive_Throws()
{
    // Arrange
    var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
    tenant.CompleteProvisioning(CreatedAt.AddMinutes(1));

    // Act
    var act = () => tenant.Resume(CreatedAt.AddMinutes(2));

    // Assert
    Assert.Throws<InvalidOperationException>(act);
}

[Fact]
public void CompleteProvisioning_Twice_Throws()
{
    // Arrange
    var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
    tenant.CompleteProvisioning(CreatedAt.AddMinutes(1));

    // Act
    var act = () => tenant.CompleteProvisioning(CreatedAt.AddMinutes(2));

    // Assert
    Assert.Throws<InvalidOperationException>(act);
}

[Fact]
public void MarkDeleted_WithoutDeprovisioning_Throws()
{
    // Arrange
    var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);
    tenant.CompleteProvisioning(CreatedAt.AddMinutes(1));

    // Act
    var act = () => tenant.MarkDeleted(CreatedAt.AddMinutes(2));

    // Assert
    Assert.Throws<InvalidOperationException>(act);
}

[Fact]
public void TheLifecycleRunsEndToEnd()
{
    // Arrange
    var tenant = Tenant.Create(TenantAlias.Create("acme"), CreatedAt);

    // Act
    tenant.CompleteProvisioning(CreatedAt.AddMinutes(1));
    tenant.Suspend(CreatedAt.AddMinutes(2));
    tenant.Resume(CreatedAt.AddMinutes(3));
    tenant.BeginDeprovisioning(CreatedAt.AddMinutes(4));
    tenant.MarkDeleted(CreatedAt.AddMinutes(5));

    // Assert
    Assert.Equal(TenantStatus.Deleted, tenant.Status);
    Assert.Equal(CreatedAt.AddMinutes(5), tenant.UpdatedAt);
}
```

Mevcut `ChangeStatus_ReplacesTheStatusAndBumpsTheTimestamp` testi silinir; yerini bu beş test alır.

- [ ] **Step 2: Testlerin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*TenantTests*'
```

Beklenen: derleme hatası, `Suspend` bulunamıyor.

- [ ] **Step 3: Geçişleri yaz**

`src/Domain/ControlPlane/Tenants/Tenant.cs` içinde `ChangeStatus` silinir ve yerine gelir:

```csharp
public void CompleteProvisioning(DateTimeOffset updatedAt)
{
    RequireStatus(updatedAt, TenantStatus.Active, TenantStatus.Provisioning);

    ProvisioningStep = null;
    ProvisioningError = null;
}

public void Suspend(DateTimeOffset updatedAt) =>
    RequireStatus(updatedAt, TenantStatus.Suspended, TenantStatus.Active);

public void Resume(DateTimeOffset updatedAt) =>
    RequireStatus(updatedAt, TenantStatus.Active, TenantStatus.Suspended);

public void BeginDeprovisioning(DateTimeOffset updatedAt) =>
    RequireStatus(updatedAt, TenantStatus.Deprovisioning, TenantStatus.Active, TenantStatus.Suspended);

public void MarkDeleted(DateTimeOffset updatedAt) =>
    RequireStatus(updatedAt, TenantStatus.Deleted, TenantStatus.Deprovisioning);

private void RequireStatus(DateTimeOffset updatedAt, TenantStatus target, params TenantStatus[] allowed)
{
    if (!allowed.Contains(Status))
    {
        throw new InvalidOperationException(
            $"A tenant cannot move from {Status} to {target}.");
    }

    Status = target;
    UpdatedAt = updatedAt;
}
```

- [ ] **Step 4: Çağrı yerlerini güncelle**

`ChangeStatus` yalnızca testlerde kullanılıyordu. `TenantSurfaceFixture`, `TenantMigrationRunnerTests` ve `TenantAccessEndpointTests` içindeki `ChangeStatus(TenantStatus.Active, ...)` çağrıları `CompleteProvisioning(...)` olur. `TenantAccessEndpointTests` `Suspended`, `Deprovisioning` ve `Deleted` durumlarını da kuruyorsa, o durumlara yaşam döngüsü sırasıyla ulaşılır: önce `CompleteProvisioning`, sonra `Suspend` veya `BeginDeprovisioning`, sonra `MarkDeleted`.

- [ ] **Step 5: Testlerin geçtiğini gör**

```bash
dotnet test --solution Api.slnx
```

Beklenen: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(domain): close the tenant status transitions

ChangeStatus accepted every pair, so a deleted tenant could be made active
again. Each transition is now a named method that refuses the wrong starting
state, which is a programming error rather than an expected failure and so
throws instead of returning a result."
```

---

### Task 7: `Role.Code` zorunlu olur, `AccessCatalog` sistem rollerini verir

**Files:**
- Modify: `src/Domain/Authorization/Role.cs`
- Modify: `src/Domain/Authorization/AccessCatalog.cs`
- Modify: `src/Infrastructure/Persistence/Tenants/Configurations/TenantRoleConfiguration.cs`
- Modify: `src/Application/Features/Provisioning/TenantProvisioningHandler.cs`
- Modify: `src/Application/Features/Members/TenantUsers.cs`, `ListMembers.cs`
- Modify: `tests/UnitTests/Authorization/AccessCatalogTests.cs`

**Interfaces:**
- Consumes: Task 4'ün `RoleId`'si.
- Produces: `Role.Code` tipi `string` (nullable değil). `AccessCatalog.OwnerRole` ve `AccessCatalog.MemberRole` — ikisi de `RoleDefinition`. `Application.Features.Members.TenantUsers.OwnerRoleCode` sabiti silinir; çağıranlar `AccessCatalog.OwnerRole.Code` kullanır.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/Authorization/AccessCatalogTests.cs` içine ekle:

```csharp
[Fact]
public void OwnerRoleIsNamedAndHoldsEveryPermission()
{
    // Arrange
    var permissionIds = AccessCatalog.Permissions.Select(permission => permission.Id).ToHashSet();

    // Act
    var owner = AccessCatalog.OwnerRole;

    // Assert
    Assert.Equal("owner", owner.Code);
    Assert.Equal(permissionIds, owner.PermissionIds);
}

[Fact]
public void MemberRoleIsNamedAndReadsOnly()
{
    // Arrange & Act
    var member = AccessCatalog.MemberRole;

    // Assert
    Assert.Equal("member", member.Code);
    Assert.Equal(2, member.PermissionIds.Count);
}

[Fact]
public void EveryRoleCarriesACode()
{
    // Arrange & Act
    var codes = AccessCatalog.Roles.Select(role => role.Code).ToList();

    // Assert
    Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
}
```

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*AccessCatalogTests*'
```

Beklenen: derleme hatası, `OwnerRole` bulunamıyor.

- [ ] **Step 3: Katalogu ve `Role`'ü düzenle**

`src/Domain/Authorization/Role.cs` içinde `public string? Code` yerine `public string Code { get; private set; } = null!;`

`src/Domain/Authorization/AccessCatalog.cs` sonuna:

```csharp
public static RoleDefinition OwnerRole { get; } = Roles.Single(role => role.Code == "owner");

public static RoleDefinition MemberRole { get; } = Roles.Single(role => role.Code == "member");
```

`TenantRoleConfiguration` içinde `Code` için `.IsRequired()` eklenir.

`TenantProvisioningHandler.SeedOwnerAsync` içindeki `AccessCatalog.Roles.Single(role => role.Code == "owner")` satırı `AccessCatalog.OwnerRole` olur. `Application/Features/Members/TenantUsers.cs` içindeki `OwnerRoleCode` sabiti silinir ve kullanıldığı yerler `AccessCatalog.OwnerRole.Code` çağırır. `ListMembers` içindeki `assignment.Code!` null bastırması kalkar.

- [ ] **Step 4: Migration'ı yeniden üret**

Kolon nullable'dan zorunluya geçtiği için şema değişir.

```bash
dotnet build Api.slnx
rm -rf src/Infrastructure/Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context TenantDbContext
```

Beklenen: son komut değişiklik olmadığını söyler.

- [ ] **Step 5: Testleri çalıştır**

```bash
docker compose down -v && docker compose up -d
dotnet test --solution Api.slnx
```

Beklenen: PASS. Testcontainers her test için taze veritabanı kurar; yerel Docker sıfırlaması eski tenant veritabanlarını temizler.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(domain): require a code on every role

The nullable code held a place for tenant defined roles that do not exist yet
and made every reader wonder when it could be absent. A tenant defined role
will carry a slug of its own name, so the column can be required today and the
invitation can keep pointing at a role by code."
```

---

### Task 8: İlişki sınıfları kalkar, `User.Roles` ve `Role.Permissions` gelir

**Files:**
- Delete: `src/Domain/Authorization/UserRole.cs`, `src/Domain/Authorization/RolePermission.cs`
- Delete: `src/Infrastructure/Persistence/Tenants/Configurations/TenantUserRoleConfiguration.cs`, `TenantRolePermissionConfiguration.cs`
- Modify: `src/Domain/Authorization/User.cs`, `Role.cs`
- Modify: `src/Infrastructure/Persistence/Tenants/TenantDbContext.cs`, `Configurations/TenantUserConfiguration.cs`, `Configurations/TenantRoleConfiguration.cs`
- Modify: `src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `src/Application/Features/Members/{TenantUsers,ListMembers,ReplaceMemberRoles,RevokeMember}.cs`, `src/Application/Features/Me/AcceptInvitation.cs`, `src/Application/Features/Provisioning/TenantProvisioningHandler.cs`
- Modify: `tests/UnitTests/Authorization/UserTests.cs`, `tests/IntegrationTests/*`

**Interfaces:**
- Consumes: Task 4 ve Task 7.
- Produces:
  - `User.Roles` tipi `IReadOnlyCollection<Role>`, `roles` adlı özel alanla desteklenir.
  - `User.AssignRoles(IEnumerable<Role> replacement)` — mevcut kümeyi tamamen değiştirir.
  - `Role.Permissions` tipi `IReadOnlyCollection<Permission>`.
  - `TenantDbContext.UserRoles` ve `TenantDbContext.RolePermissions` DbSet'leri silinir.

- [ ] **Step 1: Başarısız testi yaz**

`tests/UnitTests/Authorization/UserTests.cs` içine ekle:

```csharp
[Fact]
public void AssignRoles_ReplacesTheWholeSet()
{
    // Arrange
    var user = User.Create(ExternalUserId.Create("user_2abc123"), UserStatus.Active);
    var owner = new Role();
    var member = new Role();
    user.AssignRoles([owner]);

    // Act
    user.AssignRoles([member]);

    // Assert
    Assert.Single(user.Roles);
    Assert.Same(member, user.Roles.Single());
}

[Fact]
public void AssignRoles_WithAnEmptySet_LeavesNoRoles()
{
    // Arrange
    var user = User.Create(ExternalUserId.Create("user_2abc123"), UserStatus.Active);
    user.AssignRoles([new Role()]);

    // Act
    user.AssignRoles([]);

    // Assert
    Assert.Empty(user.Roles);
}
```

`Role`'ün parametresiz kurucusu bu test için `internal` değil `public` kalır; EF de onu kullanır.

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/UnitTests/UnitTests.csproj --filter-method '*AssignRoles*'
```

Beklenen: derleme hatası, `AssignRoles` bulunamıyor.

- [ ] **Step 3: Navigasyonları domaine ekle**

`src/Domain/Authorization/User.cs`:

```csharp
private readonly List<Role> roles = [];

public IReadOnlyCollection<Role> Roles => roles;

public void AssignRoles(IEnumerable<Role> replacement)
{
    ArgumentNullException.ThrowIfNull(replacement);

    roles.Clear();
    roles.AddRange(replacement);
}
```

`src/Domain/Authorization/Role.cs`:

```csharp
private readonly List<Permission> permissions = [];

public IReadOnlyCollection<Permission> Permissions => permissions;
```

İki ilişki sınıfı silinir.

- [ ] **Step 4: EF eşlemesini skip navigation'a çevir**

`TenantUserConfiguration` içine:

```csharp
builder.HasMany(x => x.Roles)
    .WithMany()
    .UsingEntity<Dictionary<string, object>>(
        "user_roles",
        right => right.HasOne<Role>().WithMany().HasForeignKey("role_id").OnDelete(DeleteBehavior.Cascade),
        left => left.HasOne<User>().WithMany().HasForeignKey("user_id").OnDelete(DeleteBehavior.Cascade),
        join => join.HasKey("user_id", "role_id"));
builder.Navigation(x => x.Roles).HasField("roles").UsePropertyAccessMode(PropertyAccessMode.Field);
```

`TenantRoleConfiguration` içine:

```csharp
builder.HasMany(x => x.Permissions)
    .WithMany()
    .UsingEntity<Dictionary<string, object>>(
        "role_permissions",
        right => right.HasOne<Permission>().WithMany().HasForeignKey("permission_id").OnDelete(DeleteBehavior.Cascade),
        left => left.HasOne<Role>().WithMany().HasForeignKey("role_id").OnDelete(DeleteBehavior.Cascade),
        join =>
        {
            join.HasKey("role_id", "permission_id");
            join.HasData(AccessCatalog.Roles.SelectMany(role =>
                role.PermissionIds.Select(permissionId => new
                {
                    role_id = role.Id,
                    permission_id = permissionId
                })));
        });
builder.Navigation(x => x.Permissions).HasField("permissions").UsePropertyAccessMode(PropertyAccessMode.Field);
```

İki eski yapılandırma dosyası silinir. `TenantDbContext` içindeki `UserRoles` ve `RolePermissions` DbSet'leri kaldırılır.

- [ ] **Step 5: Çağrı yerlerini sadeleştir**

`TenantAccessMiddleware` içindeki elle yazılmış iki `Join` şununla değişir:

```csharp
var tenantUser = await tenantDbContext.Users
    .Include(user => user.Roles)
    .ThenInclude(role => role.Permissions)
    .SingleOrDefaultAsync(user => user.ExternalUserId == userId, context.RequestAborted);
```

ve izin kodları:

```csharp
var permissions = tenantUser.Roles
    .SelectMany(role => role.Permissions)
    .Select(permission => permission.Code)
    .Distinct()
    .ToList();
```

`ReplaceMemberRolesHandler` ilişki satırlarını silip eklemeyi bırakır:

```csharp
user.AssignRoles(roles);
```

`RevokeMemberHandler` rol kaldırmayı `user.AssignRoles([])` ile yapar. `AcceptInvitationHandler` ve `TenantProvisioningHandler.SeedOwnerAsync` içindeki `UserRoles.Add(...)` çağrıları `user.AssignRoles([...])` veya mevcut kümeye ekleme şeklinde yeniden yazılır; ikisi de önce `Include(user => user.Roles)` ile yükler. `TenantUsers.LoadOwnerRosterAsync` sorgusu şuna iner:

```csharp
var ownerIds = await tenantDbContext.Users
    .Where(user => user.Status == UserStatus.Active
                   && user.Roles.Any(role => role.Code == AccessCatalog.OwnerRole.Code))
    .Select(user => user.Id)
    .ToListAsync(cancellationToken);
```

`ListMembersHandler` ikinci sorgusunu bırakır ve `Include` ile tek sorguya iner.

`LoadOwnerRosterAsync` ve `OwnerRoster` bu görevde yalnızca derlenir halde tutulur; ikisi de Task 10'da silinir. Burada onları yeniden yazmak kaçınılmazdır, çünkü dayandıkları `UserRoles` DbSet'i bu görevde kalkıyor.

- [ ] **Step 6: Şemanın aynı kaldığını kanıtla**

```bash
dotnet build Api.slnx
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context TenantDbContext
```

Beklenen: değişiklik yok. Kısıt adları veya tohum şekli yüzünden değişiklik bildirirse tek migration yeniden üretilir:

```bash
rm -rf src/Infrastructure/Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations
```

Üretilen migration açılıp `user_roles` ve `role_permissions` tablolarının kolon adlarının hâlâ `user_id`, `role_id`, `permission_id` olduğu ve `role_permissions` tohumunun beş sistem izniyle iki rolün kesişimini içerdiği gözle doğrulanır.

- [ ] **Step 7: Testleri çalıştır**

```bash
dotnet test --solution Api.slnx
```

Beklenen: PASS. Özellikle `MemberEndpointTests`, `RoleEndpointTests` ve `TenantAccessEndpointTests` izin claim'lerinin aynı üretildiğini kanıtlar.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(domain): let a user carry its own roles

The two link classes had no identity of their own, so they were rows pretending
to be domain types, and three handlers had to add and remove them by hand. EF
maps the same two tables as skip navigations, which leaves the schema untouched
and turns the permission lookup in the middleware into one expression."
```

---

### Task 9: Tenant'taki `User` e-posta taşır

**Files:**
- Modify: `src/Domain/Authorization/User.cs`
- Modify: `src/Infrastructure/Persistence/Tenants/Configurations/TenantUserConfiguration.cs`
- Modify: `src/Infrastructure/Messaging/TenantProvisioningRequested.cs`
- Modify: `src/ControlPlane/TenantEndpoints.cs`
- Modify: `src/Application/Features/Provisioning/TenantProvisioningHandler.cs`
- Modify: `src/Application/Features/Me/AcceptInvitation.cs`
- Modify: `src/Application/Features/Members/ListMembers.cs`
- Modify: `apps/api/openapi/Api.json`, `packages/api-client/src/schema.d.ts`
- Modify: `tests/UnitTests/Authorization/UserTests.cs` (`User.Create` artık üç parametre alır)
- Modify: `tests/IntegrationTests/TenantSurfaceFixture.cs`, `MemberEndpointTests.cs`, `TenantProvisioningHandlerTests.cs`, `TenantProvisioningEndpointTests.cs`, `InvitationAcceptanceTests.cs`, `UnitOfWorkBehaviorTests.cs`

**Interfaces:**
- Consumes: Task 4, Task 8.
- Produces:
  - `User.Create(ExternalUserId externalUserId, EmailAddress email, UserStatus status)`
  - `User.Email` tipi `EmailAddress`
  - `TenantMember(string ExternalUserId, string Email, string Status, string[] RoleCodes)`
  - `TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId, string OwnerEmail)`

- [ ] **Step 1: Başarısız testi yaz**

`tests/IntegrationTests/MemberEndpointTests.cs` içine, dosyadaki mevcut desene uyan yeni bir test:

```csharp
[Fact]
public async Task GetMembers_ReturnsTheEmailOfEachMember()
{
    // Arrange
    await using var fixture = await TenantSurfaceFixture.StartAsync();

    // Act
    using var response = await fixture.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
    var only = Assert.Single(body!);
    Assert.Equal(TenantSurfaceFixture.OwnerEmail, only.GetProperty("email").GetString());
}
```

`TenantSurfaceFixture.OwnerEmail` sabiti (`owner@example.com`) zaten var; fixture bugün onu yalnızca davet tarafında kullanıyor. Bu görevde fixture tenant kullanıcısını yaratırken de aynı değeri verir.

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/IntegrationTests/IntegrationTests.csproj --filter-method '*GetMembers_ReturnsTheEmailOfEachMember*'
```

Beklenen: derleme hatası, `TenantMember.Email` yok.

- [ ] **Step 3: E-postayı domaine ve şemaya ekle**

`User`:

```csharp
public EmailAddress Email { get; private set; } = null!;

public static User Create(ExternalUserId externalUserId, EmailAddress email, UserStatus status)
{
    ArgumentNullException.ThrowIfNull(externalUserId);
    ArgumentNullException.ThrowIfNull(email);

    return new User
    {
        Id = UserId.New(),
        ExternalUserId = externalUserId,
        Email = email,
        Status = status
    };
}
```

`TenantUserConfiguration`:

```csharp
builder.Property(x => x.Email)
    .HasColumnName("email")
    .HasMaxLength(320)
    .HasConversion(email => email.Value, value => EmailAddress.Create(value))
    .IsRequired();
```

- [ ] **Step 4: E-postayı iki yaratma yoluna taşı**

Kabul yolunda e-posta zaten elde: `AcceptInvitationHandler` `invitation.Email` değerini `User.Create`'e verir.

Provisioning yolunda yok. `TenantProvisioningRequested` bir alan kazanır ve `TenantEndpoints.Enqueue` onu platform admin'in kendi token'ından okur:

```csharp
private static void Enqueue(
    ControlPlaneDbContext dbContext,
    TenantId tenantId,
    ClaimsPrincipal user,
    DateTimeOffset now) =>
    dbContext.OutboxMessages.Add(OutboxMessage.Create(
        Guid.CreateVersion7(),
        TenantProvisioningRequested.MessageType,
        JsonSerializer.Serialize(new TenantProvisioningRequested(
            tenantId.Value,
            user.FindFirstValue("sub")!,
            user.FindFirstValue("email")!)),
        now));
```

`CreateAsync` ve `RetryAsync`, `email` claim'i yoksa kapalı tarafa düşer ve tenant yaratmaz:

```csharp
if (string.IsNullOrWhiteSpace(user.FindFirstValue("email")))
{
    return Results.BadRequest(new
    {
        error = "The access token must carry an 'email' claim. Add it to the Clerk session token."
    });
}
```

`SeedOwnerAsync` gelen e-postayı `EmailAddress.Create(message.OwnerEmail)` ile çevirip `User.Create`'e verir.

- [ ] **Step 5: Yanıtı ve istemciyi güncelle**

`ListMembers` yanıt kaydı `TenantMember(string ExternalUserId, string Email, string Status, string[] RoleCodes)` olur ve handler `user.Email.Value` doldurur.

```bash
rm -rf src/Infrastructure/Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations
cd ../.. && pnpm --filter api build && pnpm --filter @st/api-client build && cd apps/api
```

`apps/api/openapi/Api.json` ve `packages/api-client/src/schema.d.ts` aynı commit'e girer (karar 8).

- [ ] **Step 6: Testleri çalıştır**

```bash
docker compose down -v && docker compose up -d
dotnet test --solution Api.slnx
```

Beklenen: PASS.

- [ ] **Step 7: `appsettings.Development.example.json` kontrolü**

Yeni bir konfigürasyon anahtarı eklenmedi; dosya değişmez. Bu adımda yalnızca doğrulama yapılır:

```bash
git diff --stat src/Api/appsettings.Development.example.json
```

Beklenen: boş çıktı.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(domain): store the email on the tenant user

The member list returned opaque identity provider subjects, which no screen can
show. The email also gives a way back if the identity provider ever changes,
since every tenant user row today hangs on a subject we do not own."
```

---

### Task 10: `OwnerRoster` silinir, son owner korkuluğu Application'a iner

**Files:**
- Delete: `src/Domain/Authorization/OwnerRoster.cs`, `tests/UnitTests/Authorization/OwnerRosterTests.cs`
- Create: `src/Application/Features/Members/Owners.cs`
- Modify: `src/Application/Features/Members/{TenantUsers,RevokeMember,SetMemberStatus,ReplaceMemberRoles}.cs`
- Modify: `tests/IntegrationTests/MemberEndpointTests.cs`

**Interfaces:**
- Consumes: Task 8'in `User.Roles` navigasyonu, Task 7'nin `AccessCatalog.OwnerRole`.
- Produces: `internal static class Application.Features.Members.Owners` ile
  `static Task<bool> IsLastOwnerAsync(TenantDbContext tenantDbContext, User user, CancellationToken cancellationToken)`.
  `Domain.Authorization.OwnerRoster` artık yoktur.

- [ ] **Step 1: Kuralın bugün çalıştığını kanıtla**

Üç yolu da kapsayan testler `tests/IntegrationTests/MemberEndpointTests.cs` içinde zaten var:

- `DeleteMember_ForTheLastOwner_ReturnsConflict`
- `DisableMember_ForTheLastOwner_ReturnsConflict`
- `PutRoles_ThatWouldDropTheLastOwner_ReturnsConflict`

Bu görevde yeni test yazılmaz. Bu üç test, kuralın yeri değişirken davranışın kaybolmadığının kanıtıdır; bu yüzden önce ve sonra ayrı ayrı çalıştırılır.

```bash
dotnet test --project tests/IntegrationTests/IntegrationTests.csproj --filter-method '*LastOwner*'
```

Beklenen: PASS, 3 test. Geçtiklerini görmeden sonraki adıma geçilmez.

- [ ] **Step 2: Silinen birim testinin kapsamını doğrula**

`OwnerRoster`'ın birim testi siliniyor ve yerine yeni bir birim testi gelmiyor: `Owners.IsLastOwnerAsync` veritabanı sorgusu içerdiği için entegrasyon seviyesinde yaşar ve yukarıdaki üç test onu kapsar. `tests/UnitTests/Authorization/OwnerRosterTests.cs` dosyası kapsamını kaybetmeden silinir, çünkü kapsadığı davranışın tamamı bu üç teste taşınmış olur.

- [ ] **Step 3: Kuralı tek yardımcıya indir**

`src/Application/Features/Members/Owners.cs`:

```csharp
using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

// Son owner'ı kaybeden bir tenant yalnızca veritabanına elle müdahaleyle kurtarılabilir,
// o yüzden üç yazma yolu da buradan geçer.
internal static class Owners
{
    public static async Task<bool> IsLastOwnerAsync(
        TenantDbContext tenantDbContext,
        User user,
        CancellationToken cancellationToken)
    {
        var ownerCode = AccessCatalog.OwnerRole.Code;

        if (!user.Roles.Any(role => role.Code == ownerCode))
        {
            return false;
        }

        var otherOwners = await tenantDbContext.Users
            .Where(candidate => candidate.Id != user.Id
                                && candidate.Status == UserStatus.Active
                                && candidate.Roles.Any(role => role.Code == ownerCode))
            .CountAsync(cancellationToken);

        return otherOwners == 0;
    }
}
```

`Domain/Authorization/OwnerRoster.cs` ve birim testi silinir. `TenantUsers.LoadOwnerRosterAsync` silinir ve `TenantUsers.FindAsync` rolleri de yükler:

```csharp
public static Task<User?> FindAsync(
    TenantDbContext tenantDbContext,
    string externalUserId,
    CancellationToken cancellationToken)
{
    var id = ExternalUserId.Create(externalUserId);

    return tenantDbContext.Users
        .Include(user => user.Roles)
        .SingleOrDefaultAsync(user => user.ExternalUserId == id, cancellationToken);
}
```

Üç handler'daki `roster.IsLastOwner(user.Id)` çağrıları
`await Owners.IsLastOwnerAsync(tenantDbContext, user, cancellationToken)` olur.
`ReplaceMemberRolesHandler`'da kontrol yalnızca yeni küme owner içermiyorsa yapılır; bu mevcut mantık korunur.

- [ ] **Step 4: Testlerin hâlâ geçtiğini gör**

```bash
dotnet test --solution Api.slnx
```

Beklenen: PASS, üç `LastOwner` testi dahil.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(members): keep the last owner rule as a guard rail

OwnerRoster knew the rule but did not decide anything: the query and the call
both stayed in the handlers, so the object was a wrapper as complex as its own
contents. This is a guard rail against locking yourself out rather than a domain
invariant, and it reverses decision 11 of the application layer spec."
```

---

### Task 11: Denetim damgaları interceptor'e taşınır

**Files:**
- Create: `src/Domain/Shared/IAuditable.cs`
- Create: `src/Infrastructure/Persistence/AuditInterceptor.cs`
- Modify: `src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`, `src/Infrastructure/Persistence/Tenants/TenantDbContextFactory.cs`, `src/Infrastructure/Provisioning/TenantProvisioner.cs`
- Modify: `src/Domain/ControlPlane/Tenants/Tenant.cs`, `src/Domain/ControlPlane/Memberships/Membership.cs`, `src/Domain/ControlPlane/Invitations/Invitation.cs`, `src/Domain/ControlPlane/Administration/PlatformAdmin.cs`, `src/Domain/Authorization/User.cs`
- Modify: `src/Infrastructure/Persistence/*/Configurations/*.cs`
- Modify: `src/ControlPlane/TenantEndpoints.cs`, `src/Application/Features/**`
- Modify: ilgili birim ve entegrasyon testleri

**Interfaces:**
- Consumes: bütün önceki görevler.
- Produces:
  - `public interface Domain.Shared.IAuditable { DateTimeOffset CreatedAt { get; } DateTimeOffset UpdatedAt { get; } }`
  - `Tenant.Create(TenantAlias alias)` — zaman parametresi yok
  - `Tenant.RenameAlias(TenantAlias alias)`, `CompleteProvisioning()`, `Suspend()`, `Resume()`, `BeginDeprovisioning()`, `MarkDeleted()`, `RecordProvisioningProgress(TenantProvisioningStep step)`, `RecordProvisioningFailure(TenantProvisioningStep step, string error)` — hepsi zamansız
  - `Invitation.Create(TenantId tenantId, EmailAddress email, string roleCode, string tokenHash, ExternalUserId invitedBy, DateTimeOffset expiresAt)` — `createdAt` ve `lifetime` yerine hazır `expiresAt`
  - `Invitation.Accept(ExternalUserId acceptedBy, DateTimeOffset acceptedAt)` — değişmez, kabul zamanı domain verisidir
  - `PlatformAdmin.Create(ExternalUserId externalUserId)`
  - `AuditInterceptor(TimeProvider timeProvider)`

- [ ] **Step 1: Başarısız testi yaz**

`tests/IntegrationTests/AuditStampTests.cs` (yeni dosya, `ControlPlaneFixture` kullanır):

```csharp
[Fact]
public async Task SavingAnEntityStampsCreatedAndUpdated()
{
    // Arrange
    await using var dbContext = fixture.CreateControlPlaneDbContext();
    var tenant = Tenant.Create(TenantAlias.Create("acme"));

    // Act
    dbContext.Tenants.Add(tenant);
    await dbContext.SaveChangesAsync();

    // Assert
    Assert.NotEqual(default, tenant.CreatedAt);
    Assert.Equal(tenant.CreatedAt, tenant.UpdatedAt);
}

[Fact]
public async Task SavingAChangeMovesUpdatedButNotCreated()
{
    // Arrange
    await using var dbContext = fixture.CreateControlPlaneDbContext();
    var tenant = Tenant.Create(TenantAlias.Create("globex"));
    dbContext.Tenants.Add(tenant);
    await dbContext.SaveChangesAsync();
    var createdAt = tenant.CreatedAt;
    fixture.Clock.Advance(TimeSpan.FromMinutes(5));

    // Act
    tenant.RenameAlias(TenantAlias.Create("globex-two"));
    await dbContext.SaveChangesAsync();

    // Assert
    Assert.Equal(createdAt, tenant.CreatedAt);
    Assert.Equal(createdAt.AddMinutes(5), tenant.UpdatedAt);
}
```

`ControlPlaneFixture` bir `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) örneğini `Clock` olarak açar ve onu `AuditInterceptor`'e verir. Paket `IntegrationTests.csproj` içinde yoksa eklenir.

- [ ] **Step 2: Testin başarısız olduğunu gör**

```bash
dotnet test --project tests/IntegrationTests/IntegrationTests.csproj --filter-method '*AuditStampTests*'
```

Beklenen: derleme hatası, `Tenant.Create` tek parametreyle çağrılamıyor.

- [ ] **Step 3: `IAuditable` ve interceptor'ü yaz**

`src/Domain/Shared/IAuditable.cs`:

```csharp
namespace Domain.Shared;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }
}
```

`src/Infrastructure/Persistence/AuditInterceptor.cs`:

```csharp
using Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence;

public sealed class AuditInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State is EntityState.Added)
            {
                entry.CurrentValues[nameof(IAuditable.CreatedAt)] = now;
                entry.CurrentValues[nameof(IAuditable.UpdatedAt)] = now;
            }
            else if (entry.State is EntityState.Modified)
            {
                entry.CurrentValues[nameof(IAuditable.UpdatedAt)] = now;
            }
        }
    }
}
```

- [ ] **Step 4: Entity'lerden zaman parametrelerini kaldır**

Beş entity `IAuditable` uygular ve `CreatedAt` ile `UpdatedAt` özelliklerini `{ get; private set; }` olarak taşır. `Membership`, `PlatformAdmin` ve `User` bu iki özelliği yeni kazanır.

Bütün factory ve mutasyon metotlarından denetim amaçlı `DateTimeOffset` parametreleri silinir. Silinmeyen iki yer vardır ve ikisi de domain verisidir, denetim değil: `Invitation` artık hazır bir `expiresAt` alır ve `Invitation.Accept` kabul zamanını almaya devam eder.

`Tenant.RequireStatus` de zaman parametresini bırakır:

```csharp
private void RequireStatus(TenantStatus target, params TenantStatus[] allowed)
{
    if (!allowed.Contains(Status))
    {
        throw new InvalidOperationException($"A tenant cannot move from {Status} to {target}.");
    }

    Status = target;
}
```

Çağıran tarafta `TenantEndpoints`, `TenantProvisioningHandler`, `CreateInvitationHandler` ve `AcceptInvitationHandler` artık `timeProvider` kullandıkları yerleri gözden geçirir; yalnızca `expiresAt` hesabı ve kabul zamanı için tutarlar.

- [ ] **Step 5: Kolonları ve kaydı ekle**

`MembershipConfiguration`, `PlatformAdminConfiguration` ve `TenantUserConfiguration` içine:

```csharp
builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
```

`InvitationConfiguration` ve `TenantConfiguration` eksik olan `updated_at` kolonunu tamamlar.

`PostgresServiceCollectionExtensions.AddTenantPersistence` içinde interceptor kaydedilir:

```csharp
services.AddSingleton<AuditInterceptor>();
services.AddDbContext<ControlPlaneDbContext>((provider, options) =>
    options
        .UseNpgsql(
            RequiredConnectionString(configuration, "ControlPlane"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
        .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
```

`TenantDbContextFactory` ve `TenantProvisioner` de kendi `TenantDbContext` örneklerini kurarken aynı interceptor'ü ekler; ikisi de kurucularında bir `AuditInterceptor` alır. Kayıt yerleri buna göre güncellenir.

- [ ] **Step 6: Migration'ları yeniden üret**

```bash
dotnet build Api.slnx
rm -rf src/Infrastructure/Persistence/ControlPlane/Migrations src/Infrastructure/Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations add InitialControlPlane --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api --context TenantDbContext
```

Beklenen: son iki komut değişiklik olmadığını söyler. Her iki context için de tek bir migration dosyası kalmalıdır:

```bash
ls src/Infrastructure/Persistence/ControlPlane/Migrations
ls src/Infrastructure/Persistence/Tenants/Migrations
```

- [ ] **Step 7: Her şeyi sıfırdan doğrula**

```bash
docker compose down -v && docker compose up -d
docker exec -i st-postgres psql -U postgres -v migrator_password=dev_migrator -v provisioner_password=dev_provisioner -v control_password=dev_control -v tenant_password=dev_tenant -f - < scripts/bootstrap-roles.sql
dotnet run --project src/Migrator -- migrate control-plane
docker exec -i st-postgres psql -U postgres -d control_plane -f - < scripts/grant-control-plane.sql
dotnet test --solution Api.slnx
```

Beklenen: PASS. `TenantIsolationTests` içindeki kimlik sınırı testleri değişmeden geçmelidir.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(persistence): stamp audit times from an interceptor

Tenant and invitation carried times while membership and the tenant user
carried none, and every factory took a timestamp it only stored. The interceptor
writes them from TimeProvider, so no entity exposes a settable time and no
handler has to remember one. Times that are domain facts stay as parameters:
when an invitation expires, and when it was accepted."
```

---

## Faz 1 bitiş kontrolü

- [ ] `git log --oneline` on bir görev commit'ini gösteriyor.
- [ ] `src/Domain` altında `Access` klasörü yok, `Shared`, `ControlPlane` ve `Authorization` var.
- [ ] `grep -rn 'TenantUser\|TenantRole\|TenantPermission\b\|OwnerRoster\|ChangeStatus' --include='*.cs' src` yalnızca `TenantPermissions` sabitler sınıfını buluyor.
- [ ] Her context için tek bir migration dosyası var.
- [ ] `dotnet test --solution Api.slnx` yeşil.
- [ ] `apps/api/openapi/Api.json` ve `packages/api-client/src/schema.d.ts` aynı commit'lerde güncellendi.

## Faz 2 (bu planda değil)

Faz 2 kendi planını alır ve Faz 1 birleştikten sonra yazılır: `AggregateRoot<TId>`, `IDomainEvent`, event dağıtımı, `InvitationCreated`, token'ın kalkması, kabulün doğrulanmış e-posta claim'ine bağlanması, `role_codes`'un çoğullaşması, `InvitationAcceptance` değer nesnesi ve Clerk davetinin outbox üzerinden gönderilmesi.
