# Control Plane Temeli — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `apps/api`'yi database-per-tenant control plane temeline taşımak: doğru isimlendirme, tenant yaşam döngüsü, credential ile zorlanan güvenlik sınırları ve deploy adımı olarak migration.

**Architecture:** Control plane verisi `control_plane` veritabanındaki `control` şemasında; her tenant kendi `tenant_<guid-N>` veritabanında. Dört ayrı PostgreSQL login rolü güvenlik sınırını kodun dışında zorluyor. Migration'lar ayrı bir konsol projesinden deploy adımı olarak çalışıyor; tenant veritabanı yaratma işi migrator'ın değil, provisioning'in (Spec 2).

**Tech Stack:** .NET 10, PostgreSQL 18, EF Core 10 (code-first), Dapper, Npgsql, xUnit v3 + Microsoft.Testing.Platform, Testcontainers.

**Spec:** [2026-09-09-control-plane-temeli-design.md](../specs/2026-09-09-control-plane-temeli-design.md)

## Global Constraints

- Hedef framework `net10.0`, SDK `global.json` ile sabitlenmiş (`10.0.102`).
- Tablo ve kolon adları `snake_case`; GUID birincil anahtarlar `ValueGeneratedNever()`.
- Value object'ler EF'e `HasConversion` ile bağlanır.
- Testler xUnit v3 kullanır, `TestContext.Current.CancellationToken` ile; iptal token'ı elle üretilmez.
- Testlerde açık `// Arrange`, `// Act`, `// Assert` etiketleri zorunludur — bu, "gereksiz yorum yazma" kuralının tek istisnasıdır.
- Kalıcılık ve izolasyon testleri gerçek PostgreSQL'e karşı Testcontainers ile çalışır; SQLite veya in-memory sahte kullanılmaz.
- Domain katmanı ASP.NET Core, EF Core ve dış servislerden bağımsız kalır.
- Zaman damgaları `timestamptz` ve UTC; `DateTimeOffset.UtcNow` backend'de üretilir, istemciden gelmez.
- Generic repository ve spekülatif soyutlama eklenmez.
- Kod yorumları yalnızca aşikâr olmayan kısıtı açıklar; kodu tekrar etmez.

## Dosya Yapısı

**Yeni projeler**
- `src/ControlPlane/ControlPlane.csproj` — platform admin yetkilendirmesi ve `/admin/api/v1` endpoint'leri
- `src/Migrator/Migrator.csproj` — deploy adımı olarak çalışan konsol migrator
- `tests/TenantIsolationTests/TenantIsolationTests.csproj` — izolasyon ve credential sınırı testleri

**Yeni dosyalar**
- `compose.yaml` — lokal PostgreSQL
- `scripts/bootstrap-roles.sql` — dört login rolü
- `scripts/grant-control-plane.sql` — `control` şeması üzerindeki grant'lar
- `src/Domain/Tenants/TenantDatabaseName.cs` — veritabanı adı value object'i
- `src/Domain/Tenants/TenantStatus.cs` — yaşam döngüsü durumu
- `src/Domain/Access/PlatformAdmin.cs` — platform admin kaydı
- `src/Infrastructure/Persistence/Tenants/TenantSchemaMigrator.cs` — "verilen veritabanına tenant migration'larını uygula"; migrator ve (Spec 2'de) provisioning worker'ı bunu paylaşır
- `src/Infrastructure/Tenants/TenantResolver.cs` — Dapper ile cache'li alias çözümlemesi

**Yeniden adlandırılan**
- `Persistence/Admin/**` → `Persistence/ControlPlane/**`; `AdminDbContext` → `ControlPlaneDbContext`

**Silinen**
- `Persistence/Admin/Migrations/*` ve `Persistence/Tenants/Migrations/*` (squash, Görev 3)
- `PostgresTenantDatabaseMigrator.cs` — sorumluluğu `TenantSchemaMigrator` ve Migrator projesine bölünüyor
- `PostgresTenantDatabaseProvisioner.cs` — tenant veritabanı yaratma Spec 2'ye ait; Görev 9'da kaldırılır

---

### Görev 1: Lokal PostgreSQL ve geliştirme yapılandırması

**Files:**
- Create: `compose.yaml`
- Modify: `apps/api/src/Api/appsettings.Development.json`
- Modify: `apps/api/AGENTS.md`

**Interfaces:**
- Produces: `localhost:5432` üzerinde `postgres` superuser'ı ile erişilebilen PostgreSQL 18; sonraki tüm görevler buna bağlanır.

- [ ] **Step 1: `compose.yaml` dosyasını oluştur**

`apps/api/compose.yaml`:

```yaml
services:
  postgres:
    image: postgres:18-alpine
    container_name: st-postgres
    environment:
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: postgres
    ports:
      - "5432:5432"
    volumes:
      - st-postgres-data:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  st-postgres-data:
```

- [ ] **Step 2: Veritabanını başlat ve hazır olmasını doğrula**

Run: `cd apps/api && docker compose up -d && docker compose ps`
Expected: `st-postgres` servisi `healthy` durumunda.

- [ ] **Step 3: Bağlantıyı doğrula**

Run: `docker exec st-postgres psql -U postgres -c "SELECT version();"`
Expected: `PostgreSQL 18...` satırı yazdırılır.

- [ ] **Step 4: Geliştirme yapılandırmasını lokale çevir**

`apps/api/src/Api/appsettings.Development.json` içeriğini tamamen şununla değiştir. Neon bağlantı dizesi ileride bir deploy ortamı olarak ele alınacak; bu dosya gitignore'da olduğu için commit edilmez.

```json
{
  "ConnectionStrings": {
    "ControlPlane": "Host=localhost;Port=5432;Database=control_plane;Username=st_control;Password=dev_control",
    "ControlPlaneRead": "Host=localhost;Port=5432;Database=control_plane;Username=st_tenant;Password=dev_tenant",
    "TenantData": "Host=localhost;Port=5432;Username=st_tenant;Password=dev_tenant"
  },
  "Clerk": {
    "Issuer": "",
    "Audience": ""
  }
}
```

- [ ] **Step 5: `appsettings.json` şablonunu aynı şekle getir**

`apps/api/src/Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ControlPlane": "",
    "ControlPlaneRead": "",
    "TenantData": ""
  },
  "Clerk": {
    "Issuer": "",
    "Audience": ""
  }
}
```

- [ ] **Step 6: `AGENTS.md` komutlarına veritabanı bölümünü ekle**

`apps/api/AGENTS.md` içindeki `## Commands` bölümünün başına ekle:

```markdown
- Start PostgreSQL: `docker compose up -d`
- Stop PostgreSQL: `docker compose down`
- Reset PostgreSQL (drops all local data): `docker compose down -v && docker compose up -d`
```

- [ ] **Step 7: Commit**

```bash
git add apps/api/compose.yaml apps/api/src/Api/appsettings.json apps/api/AGENTS.md
git commit -m "chore(api): run PostgreSQL locally for development"
```

---

### Görev 2: Domain — `TenantDatabaseName` ve tenant kaydının yeni şekli

**Files:**
- Create: `apps/api/src/Domain/Tenants/TenantDatabaseName.cs`
- Create: `apps/api/src/Domain/Tenants/TenantStatus.cs`
- Modify: `apps/api/src/Domain/Tenants/Tenant.cs`
- Test: `apps/api/tests/UnitTests/Tenants/TenantDatabaseNameTests.cs`
- Test: `apps/api/tests/UnitTests/Tenants/TenantTests.cs`

**Interfaces:**
- Produces: `TenantDatabaseName.ForTenant(Guid)`, `TenantDatabaseName.Create(string)`, `TenantDatabaseName.Value`; `TenantStatus` enum'u (`Provisioning`, `Active`, `Suspended`, `Deprovisioning`, `Deleted`); `Tenant.Create(Guid id, TenantAlias alias, DateTimeOffset createdAt)` ve `Tenant.DatabaseName`, `Tenant.Status`, `Tenant.CreatedAt`, `Tenant.UpdatedAt`.

- [ ] **Step 1: `TenantDatabaseName` için başarısız testleri yaz**

`apps/api/tests/UnitTests/Tenants/TenantDatabaseNameTests.cs`:

```csharp
using Domain.Tenants;
using Xunit;

namespace UnitTests.Tenants;

public sealed class TenantDatabaseNameTests
{
    [Fact]
    public void ForTenant_BuildsNameFromTheTenantIdentifier()
    {
        // Arrange
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");

        // Act
        var databaseName = TenantDatabaseName.ForTenant(id);

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", databaseName.Value);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("control_plane")]
    public void Create_ForAReservedDatabaseName_Throws(string value)
    {
        // Arrange & Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tenant_Upper")]
    [InlineData("tenant-with-dash")]
    [InlineData("tenant name")]
    public void Create_ForAnInvalidIdentifier_Throws(string value)
    {
        // Arrange & Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_ForANameLongerThanSixtyThreeBytes_Throws()
    {
        // Arrange
        var value = new string('a', 64);

        // Act
        var act = () => TenantDatabaseName.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_ForAValidName_KeepsTheValue()
    {
        // Arrange
        const string value = "tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9";

        // Act
        var databaseName = TenantDatabaseName.Create(value);

        // Assert
        Assert.Equal(value, databaseName.Value);
    }
}
```

- [ ] **Step 2: Testlerin derlenmediğini doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Derleme hatası — `TenantDatabaseName` tipi bulunamıyor.

- [ ] **Step 3: `TenantDatabaseName` value object'ini yaz**

`apps/api/src/Domain/Tenants/TenantDatabaseName.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Domain.Tenants;

public sealed record TenantDatabaseName
{
    private const int MaximumByteLength = 63;

    private static readonly HashSet<string> ReservedValues = new(StringComparer.Ordinal)
    {
        "postgres",
        "template0",
        "template1",
        "control_plane"
    };

    private static readonly Regex IdentifierPattern = new(
        "\\A[a-z][a-z0-9_]*\\z",
        RegexOptions.CultureInvariant);

    public string Value { get; }

    private TenantDatabaseName(string value)
    {
        Value = value;
    }

    public static TenantDatabaseName ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        return new TenantDatabaseName($"tenant_{tenantId:N}");
    }

    public static TenantDatabaseName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !IdentifierPattern.IsMatch(value) ||
            Encoding.UTF8.GetByteCount(value) > MaximumByteLength ||
            ReservedValues.Contains(value))
        {
            throw new ArgumentException(
                "Tenant database name must be a lowercase PostgreSQL identifier of at most 63 bytes.",
                nameof(value));
        }

        return new TenantDatabaseName(value);
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: `TenantDatabaseNameTests` içindeki tüm testler PASS.

- [ ] **Step 5: `Tenant` için başarısız testleri yaz**

`apps/api/tests/UnitTests/Tenants/TenantTests.cs` dosyasının tamamını şununla değiştir:

```csharp
using Domain.Tenants;
using Xunit;

namespace UnitTests.Tenants;

public sealed class TenantTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var alias = TenantAlias.Create("acme");

        // Act
        var act = () => Tenant.Create(Guid.Empty, alias, CreatedAt);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_DerivesTheDatabaseNameOnce()
    {
        // Arrange
        var id = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");

        // Act
        var tenant = Tenant.Create(id, TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal("tenant_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", tenant.DatabaseName.Value);
    }

    [Fact]
    public void Create_StartsInTheProvisioningState()
    {
        // Arrange & Act
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(CreatedAt, tenant.CreatedAt);
        Assert.Equal(CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void RenameAlias_ReplacesTheAliasAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
        var renamedAt = CreatedAt.AddMinutes(5);

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex"), renamedAt);

        // Assert
        Assert.Equal("globex", tenant.Alias.Value);
        Assert.Equal(renamedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void ChangeStatus_ReplacesTheStatusAndBumpsTheTimestamp()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
        var activatedAt = CreatedAt.AddSeconds(30);

        // Act
        tenant.ChangeStatus(TenantStatus.Active, activatedAt);

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(activatedAt, tenant.UpdatedAt);
    }
}
```

- [ ] **Step 6: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Derleme hatası — `Tenant.Create` üç parametre almıyor, `TenantStatus` bulunamıyor.

- [ ] **Step 7: `TenantStatus` enum'unu yaz**

`apps/api/src/Domain/Tenants/TenantStatus.cs`:

```csharp
namespace Domain.Tenants;

public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Deprovisioning,
    Deleted
}
```

- [ ] **Step 8: `Tenant` aggregate'ini güncelle**

`apps/api/src/Domain/Tenants/Tenant.cs` dosyasının tamamını şununla değiştir:

```csharp
namespace Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; private set; }

    public TenantAlias Alias { get; private set; } = null!;

    public TenantDatabaseName DatabaseName { get; private set; } = null!;

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Tenant()
    {
    }

    public static Tenant Create(Guid id, TenantAlias alias, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(alias);

        return new Tenant
        {
            Id = id,
            Alias = alias,
            DatabaseName = TenantDatabaseName.ForTenant(id),
            Status = TenantStatus.Provisioning,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void RenameAlias(TenantAlias alias, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(alias);

        Alias = alias;
        UpdatedAt = updatedAt;
    }

    public void ChangeStatus(TenantStatus status, DateTimeOffset updatedAt)
    {
        Status = status;
        UpdatedAt = updatedAt;
    }
}
```

- [ ] **Step 9: Birim testlerinin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Tüm testler PASS. (Çözümün tamamı bu noktada henüz derlenmez; `Tenant.Create` çağıran diğer projeler Görev 3'te düzeltilir.)

- [ ] **Step 10: Commit**

```bash
git add apps/api/src/Domain/Tenants apps/api/tests/UnitTests/Tenants
git commit -m "feat(api): give tenants a persisted database name and lifecycle status"
```

---

### Görev 3: Control plane yeniden adlandırması ve migration squash

**Files:**
- Rename: `apps/api/src/Infrastructure/Persistence/Admin/` → `apps/api/src/Infrastructure/Persistence/ControlPlane/`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContextFactory.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/TenantConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/Tenants/TenantDbContextFactory.cs`
- Modify: `apps/api/src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `apps/api/tests/IntegrationTests/*.cs`
- Delete: `apps/api/src/Infrastructure/Persistence/*/Migrations/*`

**Interfaces:**
- Consumes: Görev 2'den `Tenant.Create(Guid, TenantAlias, DateTimeOffset)`, `TenantDatabaseName`, `TenantStatus`.
- Produces: `ControlPlaneDbContext` (`control` şeması, `control_plane` veritabanı); `InitialControlPlane` ve `InitialTenantAccess` migration'ları.

- [ ] **Step 1: Klasörü ve tipleri yeniden adlandır**

```bash
cd apps/api/src/Infrastructure/Persistence
git mv Admin ControlPlane
git mv ControlPlane/AdminDbContext.cs ControlPlane/ControlPlaneDbContext.cs
git mv ControlPlane/AdminDbContextFactory.cs ControlPlane/ControlPlaneDbContextFactory.cs
```

Ardından tüm çözümde şu değişiklikleri uygula (namespace, tip adı ve şema):
- `Infrastructure.Persistence.Admin` → `Infrastructure.Persistence.ControlPlane`
- `AdminDbContext` → `ControlPlaneDbContext`
- `AdminDbContextFactory` → `ControlPlaneDbContextFactory`
- `HasDefaultSchema("admin")` → `HasDefaultSchema("control")`
- `MigrationsHistoryTable("__EFMigrationsHistory", "admin")` → `MigrationsHistoryTable("__EFMigrationsHistory", "control")`
- `Database = "systemdb"` → `Database = "control_plane"`

- [ ] **Step 2: `TenantConfiguration`'ı yeni tenant şekline göre güncelle**

`apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/TenantConfiguration.cs`:

```csharp
using Domain.Tenants;
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
        builder.HasIndex(x => x.Alias).IsUnique();
        builder.HasIndex(x => x.DatabaseName).IsUnique();
    }
}
```

- [ ] **Step 3: Bağlantı dizesi adlarını yapılandırmaya göre güncelle**

`apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs` içindeki `AddTenantPersistence` metodunu şununla değiştir. `AddPostgresReadiness` şimdilik `ControlPlane` bağlantısını kullanır; Görev 5'te credential ayrımı tamamlanır.

```csharp
public static IServiceCollection AddTenantPersistence(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddDbContext<ControlPlaneDbContext>(options =>
        options.UseNpgsql(
            RequiredConnectionString(configuration, "ControlPlane"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control")));

    services.AddScoped(_ => new TenantDbContextFactory(
        RequiredConnectionString(configuration, "TenantData")));

    return services;
}

internal static string RequiredConnectionString(IConfiguration configuration, string name) =>
    configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");
```

`AddPostgresReadiness` içindeki `configuration.GetConnectionString("Postgres")` çağrısını `configuration.GetConnectionString("ControlPlane")` yap ve `Database = "systemdb"` dönüşümünü kaldır — bağlantı dizesi zaten `control_plane`'i gösteriyor.

- [ ] **Step 4: Eski migration'ları sil**

```bash
cd apps/api
rm -rf src/Infrastructure/Persistence/ControlPlane/Migrations
rm -rf src/Infrastructure/Persistence/Tenants/Migrations
```

- [ ] **Step 5: Çözümün derlendiğini doğrula**

Run: `dotnet build apps/api/Api.slnx`
Expected: Derleme başarılı. Hata kalırsa `Tenant.Create` çağrılarına `DateTimeOffset.UtcNow` argümanını ekle ve `AdminDbContext` referanslarını düzelt.

- [ ] **Step 6: Rolleri geçici olarak atlayarak migration üret**

Migration üretimi tasarım zamanı fabrikalarını kullanır ve veritabanına bağlanmaz, ancak bağlantı dizesinin okunabilmesi gerekir. Görev 1'deki `appsettings.Development.json` bunu sağlar.

```bash
cd apps/api
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialControlPlane \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
dotnet tool run dotnet-ef migrations add InitialTenantAccess \
  --project src/Infrastructure --startup-project src/Api \
  --context TenantDbContext --output-dir Persistence/Tenants/Migrations
```

- [ ] **Step 7: Üretilen migration'ın doğru şemayı kurduğunu doğrula**

Run: `grep -n "control" apps/api/src/Infrastructure/Persistence/ControlPlane/Migrations/*_InitialControlPlane.cs | head`
Expected: `EnsureSchema(name: "control")` ve `schema: "control"` satırları görünür.

- [ ] **Step 8: Testleri güncelle ve çalıştır**

`apps/api/tests/IntegrationTests/*.cs` içindeki `AdminDbContext`, `"systemdb"` ve `"admin"` referanslarını `ControlPlaneDbContext`, `"control_plane"` ve `"control"` ile değiştir; `Tenant.Create` çağrılarına üçüncü argüman olarak `DateTimeOffset.UtcNow` ekle.

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "refactor(api): rename the control plane database and squash migrations"
```

---

### Görev 4: Veritabanı rolleri ve grant script'leri

**Files:**
- Create: `apps/api/scripts/bootstrap-roles.sql`
- Create: `apps/api/scripts/grant-control-plane.sql`
- Modify: `apps/api/AGENTS.md`

**Interfaces:**
- Produces: `st_migrator` (DDL + `CREATEDB`), `st_provisioner` (`CREATEDB`), `st_control` (control plane DML), `st_tenant` (tenant DML + control plane okuma) login rolleri.

- [ ] **Step 1: Rol bootstrap script'ini yaz**

`apps/api/scripts/bootstrap-roles.sql`:

```sql
-- Creates the four login roles. Requires a superuser connection and runs once per environment.
-- Passwords are supplied as psql variables so that no secret is stored in the repository:
--   psql -v migrator_password=... -v provisioner_password=... \
--        -v control_password=... -v tenant_password=... -f scripts/bootstrap-roles.sql

CREATE ROLE st_migrator LOGIN PASSWORD :'migrator_password' CREATEDB;
CREATE ROLE st_provisioner LOGIN PASSWORD :'provisioner_password' CREATEDB;
CREATE ROLE st_control LOGIN PASSWORD :'control_password';
CREATE ROLE st_tenant LOGIN PASSWORD :'tenant_password';
```

- [ ] **Step 2: Grant script'ini yaz**

`control_plane` veritabanı ve `control` şeması ancak migration çalıştıktan sonra var olduğu için grant'lar ayrı bir script'tedir.

`apps/api/scripts/grant-control-plane.sql`:

```sql
-- Grants control plane access to the application roles.
-- Run against the control_plane database after `migrate control-plane` has succeeded.

GRANT CONNECT ON DATABASE control_plane TO st_control, st_tenant;
GRANT USAGE ON SCHEMA control TO st_control, st_tenant;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA control TO st_control;
ALTER DEFAULT PRIVILEGES FOR ROLE st_migrator IN SCHEMA control
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO st_control;

GRANT SELECT ON control.tenants, control.memberships TO st_tenant;
```

- [ ] **Step 3: Rolleri oluştur**

```bash
cd apps/api
docker exec -i st-postgres psql -U postgres \
  -v migrator_password=dev_migrator \
  -v provisioner_password=dev_provisioner \
  -v control_password=dev_control \
  -v tenant_password=dev_tenant \
  -f - < scripts/bootstrap-roles.sql
```

- [ ] **Step 4: Rollerin oluştuğunu doğrula**

Run: `docker exec st-postgres psql -U postgres -c "SELECT rolname, rolcreatedb FROM pg_roles WHERE rolname LIKE 'st\_%' ORDER BY rolname;"`
Expected: Dört satır — `st_control` (f), `st_migrator` (t), `st_provisioner` (t), `st_tenant` (f).

- [ ] **Step 5: `AGENTS.md`'ye kurulum komutunu ekle**

`apps/api/AGENTS.md` içindeki `## Commands` bölümüne ekle:

```markdown
- Create database roles (once per environment, superuser): `docker exec -i st-postgres psql -U postgres -v migrator_password=dev_migrator -v provisioner_password=dev_provisioner -v control_password=dev_control -v tenant_password=dev_tenant -f - < scripts/bootstrap-roles.sql`
- Grant control plane access (after the control plane migration): `docker exec -i st-postgres psql -U postgres -d control_plane -f - < scripts/grant-control-plane.sql`
```

Ayrıca dosyanın kurallar listesine ekle:

```markdown
- Create database roles with the bootstrap script, never in EF migrations; the API runtime role never holds DDL privileges.
```

- [ ] **Step 6: Commit**

```bash
git add apps/api/scripts apps/api/AGENTS.md
git commit -m "feat(api): separate database roles for migration, provisioning and runtime"
```

---

### Görev 5: Credential ayrımı ve okuma bağlantısı

**Files:**
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/ConnectionConfigurationTests.cs`

**Interfaces:**
- Consumes: Görev 4'ün rolleri; Görev 3'ün `ControlPlaneDbContext`'i.
- Produces: DI'da `"control-plane-read"` anahtarlı `NpgsqlDataSource` (rol: `st_tenant`), `ControlPlaneDbContext` (rol: `st_control`), `TenantDbContextFactory` (rol: `st_tenant`).

- [ ] **Step 1: Eksik bağlantı dizesi için başarısız test yaz**

`apps/api/tests/IntegrationTests/ConnectionConfigurationTests.cs`:

```csharp
using Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace IntegrationTests;

public sealed class ConnectionConfigurationTests
{
    [Fact]
    public void AddTenantPersistence_WithoutAControlPlaneConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services.AddTenantPersistence(configuration);

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ControlPlane", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantPersistence_RegistersTheControlPlaneReadDataSource()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ControlPlane"] = "Host=localhost;Database=control_plane;Username=st_control",
                ["ConnectionStrings:ControlPlaneRead"] = "Host=localhost;Database=control_plane;Username=st_tenant",
                ["ConnectionStrings:TenantData"] = "Host=localhost;Username=st_tenant"
            })
            .Build();

        // Act
        services.AddTenantPersistence(configuration);
        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredKeyedService<NpgsqlDataSource>("control-plane-read");

        // Assert
        Assert.Contains("st_tenant", dataSource.ConnectionString, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `AddTenantPersistence_RegistersTheControlPlaneReadDataSource` FAIL — anahtarlı servis kayıtlı değil.

- [ ] **Step 3: Okuma veri kaynağını kaydet**

`apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs` içindeki `AddTenantPersistence` metoduna, `return services;` satırından önce ekle:

```csharp
services.AddKeyedSingleton<NpgsqlDataSource>("control-plane-read", (_, _) =>
    NpgsqlDataSource.Create(RequiredConnectionString(configuration, "ControlPlaneRead")));
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Her iki test de PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs apps/api/tests/IntegrationTests/ConnectionConfigurationTests.cs
git commit -m "feat(api): give each access path its own database credential"
```

---

### Görev 6: Tenant çözümleme — Dapper, cache ve status kapısı

**Files:**
- Create: `apps/api/src/Infrastructure/Tenants/TenantResolver.cs`
- Modify: `apps/api/src/Infrastructure/Infrastructure.csproj`
- Modify: `apps/api/src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `apps/api/src/Api/Tenants/TenantContext.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/TenantAccessEndpointTests.cs`

**Interfaces:**
- Consumes: Görev 5'ten `"control-plane-read"` anahtarlı `NpgsqlDataSource`.
- Produces: `ResolvedTenant(Guid Id, string Alias, string DatabaseName, TenantStatus Status)` kaydı ve `TenantResolver.ResolveAsync(string alias, CancellationToken)`.

- [ ] **Step 1: Dapper'ı ekle**

```bash
cd apps/api && dotnet add src/Infrastructure/Infrastructure.csproj package Dapper
```

- [ ] **Step 2: Status davranışı için başarısız testler yaz**

`apps/api/tests/IntegrationTests/TenantAccessEndpointTests.cs` içindeki `SeedTenantAccessAsync` yardımcı metodunu tenant status'ünü parametre alacak şekilde genişlet ve şu testleri ekle:

```csharp
[Fact]
public async Task GetWhoAmI_WhileTheTenantIsProvisioning_ReturnsServiceUnavailable()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var tenant = Tenant.Create(TenantId, TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
    await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, ExternalUser, "Active");
    await using var factory = CreateFactory(postgres.GetConnectionString());
    using var client = factory.CreateClient();

    // Act
    using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}

[Fact]
public async Task GetWhoAmI_ForASuspendedTenant_ReturnsForbidden()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var tenant = Tenant.Create(TenantId, TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
    tenant.ChangeStatus(TenantStatus.Suspended, DateTimeOffset.UtcNow);
    await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, ExternalUser, "Active");
    await using var factory = CreateFactory(postgres.GetConnectionString());
    using var client = factory.CreateClient();

    // Act
    using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task GetWhoAmI_ForADeletedTenant_ReturnsNotFound()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var tenant = Tenant.Create(TenantId, TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
    tenant.ChangeStatus(TenantStatus.Deleted, DateTimeOffset.UtcNow);
    await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, ExternalUser, "Active");
    await using var factory = CreateFactory(postgres.GetConnectionString());
    using var client = factory.CreateClient();

    // Act
    using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

Mevcut mutlu yol testinde tenant'ı `Active` yapmak için seed'den önce `tenant.ChangeStatus(TenantStatus.Active, DateTimeOffset.UtcNow);` çağır. Sınıfın başına şu sabitleri ekle:

```csharp
private static readonly Guid TenantId = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
private const string ExternalUser = "user_2abc123";
```

`CreateFactory` içindeki `builder.UseSetting("ConnectionStrings:Postgres", connectionString)` satırını üç bağlantıyı da ayarlayacak şekilde değiştir:

```csharp
builder.UseSetting("ConnectionStrings:ControlPlane", WithDatabase(connectionString, "control_plane"));
builder.UseSetting("ConnectionStrings:ControlPlaneRead", WithDatabase(connectionString, "control_plane"));
builder.UseSetting("ConnectionStrings:TenantData", connectionString);
```

- [ ] **Step 3: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Üç yeni test FAIL — middleware status'e bakmıyor, hepsi 200 veya 403 dönüyor.

- [ ] **Step 4: `TenantResolver`'ı yaz**

Tenant yolunun control plane'e yaptığı **tüm** okumalar buradan geçer; böylece bu yol `st_tenant` (salt okunur) credential'ının dışına hiç çıkmaz ve `ControlPlaneDbContext`'e (yazma yetkili `st_control`) hiç dokunmaz. Tenant kaydı cache'lenir, membership cache'lenmez — bir üyeliğin iptali anında etkili olmalıdır.

`apps/api/src/Infrastructure/Tenants/TenantResolver.cs`:

```csharp
using Dapper;
using Domain.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tenants;

public sealed record ResolvedTenant(Guid Id, string Alias, string DatabaseName, TenantStatus Status);

public sealed class TenantResolver(
    [FromKeyedServices("control-plane-read")] NpgsqlDataSource dataSource,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    public async Task<ResolvedTenant?> ResolveAsync(string alias, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(alias);

        if (cache.TryGetValue(cacheKey, out ResolvedTenant? cached))
        {
            return cached;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TenantRow>(new CommandDefinition(
            "SELECT id, alias, database_name AS databasename, status FROM control.tenants WHERE alias = @alias",
            new { alias },
            cancellationToken: cancellationToken));
        var resolved = row is null
            ? null
            : new ResolvedTenant(row.Id, row.Alias, row.DatabaseName, Enum.Parse<TenantStatus>(row.Status));

        cache.Set(cacheKey, resolved, CacheLifetime);

        return resolved;
    }

    public async Task<bool> HasMembershipAsync(
        Guid tenantId,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM control.memberships " +
            "WHERE tenant_id = @tenantId AND external_user_id = @externalUserId)",
            new { tenantId, externalUserId },
            cancellationToken: cancellationToken));
    }

    public void Evict(string alias) => cache.Remove(CacheKey(alias));

    private static string CacheKey(string alias) => $"tenant:{alias}";

    private sealed record TenantRow(Guid Id, string Alias, string DatabaseName, string Status);
}
```

- [ ] **Step 5: `TenantContext`'i sadeleştir**

`apps/api/src/Api/Tenants/TenantContext.cs` dosyasının tamamını şununla değiştir:

```csharp
namespace Api.Tenants;

public sealed class TenantContext
{
    private Guid? tenantId;
    private string? alias;

    public Guid TenantId => tenantId ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    public string Alias => alias ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    internal void Set(Guid resolvedTenantId, string resolvedAlias)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
    }
}
```

- [ ] **Step 6: `TenantDbContextFactory`'yi veritabanı adı alacak şekilde değiştir**

`apps/api/src/Infrastructure/Persistence/Tenants/TenantDbContextFactory.cs` dosyasının tamamını şununla değiştir:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName
        };
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new TenantDbContext(options);
    }
}

public sealed class TenantDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<TenantDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("TenantData")
            ?? throw new InvalidOperationException("Connection string 'TenantData' is not configured.");

        return new TenantDbContextFactory(connectionString).Create("design_time");
    }
}
```

- [ ] **Step 7: Middleware'i yeniden yaz**

`apps/api/src/Api/Tenants/TenantAccessMiddleware.cs` dosyasının tamamını şununla değiştir:

```csharp
using System.Security.Claims;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Persistence.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Api.Tenants;

public sealed class TenantAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantAlias = context.Request.RouteValues["tenantAlias"]?.ToString();

        if (string.IsNullOrWhiteSpace(tenantAlias))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync();
            return;
        }

        var externalUserId = context.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            await context.ChallengeAsync();
            return;
        }

        TenantAlias alias;

        try
        {
            alias = TenantAlias.Create(tenantAlias);
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var services = context.RequestServices;
        var resolver = services.GetRequiredService<TenantResolver>();
        var tenant = await resolver.ResolveAsync(alias.Value, context.RequestAborted);

        if (tenant is null || tenant.Status is TenantStatus.Deprovisioning or TenantStatus.Deleted)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (tenant.Status is TenantStatus.Provisioning)
        {
            context.Response.Headers.RetryAfter = "10";
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (tenant.Status is TenantStatus.Suspended)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var hasMembership = await resolver.HasMembershipAsync(
            tenant.Id,
            externalUserId,
            context.RequestAborted);

        if (!hasMembership)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var userId = ExternalUserId.Create(externalUserId);
        var tenantDbContextFactory = services.GetRequiredService<TenantDbContextFactory>();
        await using var tenantDbContext = tenantDbContextFactory.Create(tenant.DatabaseName);
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            context.RequestAborted);

        if (tenantUser?.Status != TenantUserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        tenantContext.Set(tenant.Id, tenant.Alias);
        context.User.AddIdentity(new ClaimsIdentity([new Claim("tenant_id", tenant.Id.ToString("N"))], "Tenant"));

        await next(context);
    }
}
```

- [ ] **Step 8: Servisleri kaydet**

`apps/api/src/Api/Program.cs` içine, `AddTenantPersistence` çağrısından sonra ekle:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TenantResolver>();
```

- [ ] **Step 9: Cache davranışını doğrulayan testi ekle**

`apps/api/tests/IntegrationTests/TenantResolverTests.cs`:

```csharp
using Infrastructure.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_ServesTheSecondCallFromCache()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await CreateTenantsTableAsync(postgres.GetConnectionString(), "acme");
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(dataSource, cache);
        await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);
        await DeleteTenantsAsync(postgres.GetConnectionString());

        // Act
        var cached = await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cached);
        Assert.Equal("acme", cached.Alias);
    }

    [Fact]
    public async Task Evict_ForcesTheNextCallToReadTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await CreateTenantsTableAsync(postgres.GetConnectionString(), "acme");
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(dataSource, cache);
        await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);
        await DeleteTenantsAsync(postgres.GetConnectionString());

        // Act
        resolver.Evict("acme");
        var resolved = await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
    }

    private static async Task CreateTenantsTableAsync(string connectionString, string alias)
    {
        await ExecuteAsync(connectionString, "CREATE SCHEMA control");
        await ExecuteAsync(
            connectionString,
            "CREATE TABLE control.tenants (id uuid PRIMARY KEY, alias text NOT NULL, " +
            "database_name text NOT NULL, status text NOT NULL, created_at timestamptz NOT NULL, " +
            "updated_at timestamptz NOT NULL)");
        await ExecuteAsync(
            connectionString,
            $"INSERT INTO control.tenants VALUES (gen_random_uuid(), '{alias}', 'tenant_{alias}', 'Active', now(), now())");
    }

    private static Task DeleteTenantsAsync(string connectionString) =>
        ExecuteAsync(connectionString, "DELETE FROM control.tenants");

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 10: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Status ve cache testleri dahil tüm testler PASS.

- [ ] **Step 11: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): resolve tenants through a cached read-only path and gate on status"
```

---

### Görev 7: `platform_admins` ve `RequirePlatformAdmin` policy'si

**Files:**
- Create: `apps/api/src/Domain/Access/PlatformAdmin.cs`
- Create: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/PlatformAdminConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs`
- Test: `apps/api/tests/UnitTests/Access/PlatformAdminTests.cs`

**Interfaces:**
- Produces: `PlatformAdmin.Create(Guid id, ExternalUserId externalUserId, DateTimeOffset createdAt)`; `ControlPlaneDbContext.PlatformAdmins`; `control.platform_admins` tablosu.

- [ ] **Step 1: Başarısız birim testini yaz**

`apps/api/tests/UnitTests/Access/PlatformAdminTests.cs`:

```csharp
using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class PlatformAdminTests
{
    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var act = () => PlatformAdmin.Create(Guid.Empty, externalUserId, DateTimeOffset.UtcNow);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_KeepsTheExternalUserIdentity()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var admin = PlatformAdmin.Create(Guid.NewGuid(), externalUserId, createdAt);

        // Assert
        Assert.Equal(externalUserId, admin.ExternalUserId);
        Assert.Equal(createdAt, admin.CreatedAt);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Derleme hatası — `PlatformAdmin` bulunamıyor.

- [ ] **Step 3: `PlatformAdmin` entity'sini yaz**

`apps/api/src/Domain/Access/PlatformAdmin.cs`:

```csharp
namespace Domain.Access;

public sealed class PlatformAdmin
{
    public Guid Id { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    private PlatformAdmin()
    {
    }

    public static PlatformAdmin Create(Guid id, ExternalUserId externalUserId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Platform admin ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new PlatformAdmin
        {
            Id = id,
            ExternalUserId = externalUserId,
            CreatedAt = createdAt
        };
    }
}
```

- [ ] **Step 4: EF yapılandırmasını yaz**

`apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/PlatformAdminConfiguration.cs`:

```csharp
using Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> builder)
    {
        builder.ToTable("platform_admins");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ExternalUserId)
            .HasColumnName("external_user_id")
            .HasMaxLength(255)
            .HasConversion(userId => userId.Value, value => ExternalUserId.Create(value))
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => x.ExternalUserId).IsUnique();
    }
}
```

- [ ] **Step 5: `DbSet`'i ekle ve migration üret**

`ControlPlaneDbContext` içine ekle:

```csharp
public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
```

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add AddPlatformAdmins \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
```

- [ ] **Step 6: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 7: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): record platform admins in the control plane"
```

---

### Görev 8: `src/ControlPlane` projesi ve platform admin yetkilendirmesi

**Files:**
- Create: `apps/api/src/ControlPlane/ControlPlane.csproj`
- Create: `apps/api/src/ControlPlane/Authorization/PlatformAdminRequirement.cs`
- Create: `apps/api/src/ControlPlane/Authorization/PlatformAdminHandler.cs`
- Create: `apps/api/src/ControlPlane/ControlPlaneEndpoints.cs`
- Modify: `apps/api/src/Api/Api.csproj`
- Modify: `apps/api/src/Api/Program.cs`
- Modify: `apps/api/Api.slnx`
- Test: `apps/api/tests/IntegrationTests/PlatformAdminAuthorizationTests.cs`

**Interfaces:**
- Consumes: Görev 7'den `ControlPlaneDbContext.PlatformAdmins`.
- Produces: `"RequirePlatformAdmin"` policy adı; `IServiceCollection.AddControlPlane()`; `IEndpointRouteBuilder.MapControlPlane()`; `GET /admin/api/v1/tenants` endpoint'i.

- [ ] **Step 1: Projeyi oluştur ve çözüme ekle**

```bash
cd apps/api
dotnet new classlib -o src/ControlPlane -n ControlPlane -f net10.0
rm src/ControlPlane/Class1.cs
dotnet add src/ControlPlane/ControlPlane.csproj reference src/Infrastructure/Infrastructure.csproj src/Domain/Domain.csproj
dotnet add src/Api/Api.csproj reference src/ControlPlane/ControlPlane.csproj
dotnet sln Api.slnx add src/ControlPlane/ControlPlane.csproj
```

`src/ControlPlane/ControlPlane.csproj` içine ASP.NET Core tiplerine erişim için ekle:

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 2: Başarısız yetkilendirme testini yaz**

`apps/api/tests/IntegrationTests/PlatformAdminAuthorizationTests.cs`:

```csharp
using System.Net;
using Xunit;

namespace IntegrationTests;

public sealed class PlatformAdminAuthorizationTests
{
    [Fact]
    public async Task GetTenants_ForANonPlatformAdmin_ReturnsForbidden()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);

        // Act
        using var response = await fixture.Client.GetAsync("/admin/api/v1/tenants", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenants_ForAPlatformAdmin_ReturnsOk()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.GetAsync("/admin/api/v1/tenants", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

`apps/api/tests/IntegrationTests/ControlPlaneFixture.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Access;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class ControlPlaneFixture : IAsyncDisposable
{
    public const string TestUserId = "user_2abc123";

    private readonly PostgreSqlContainer postgres;
    private readonly WebApplicationFactory<Program> factory;

    private ControlPlaneFixture(PostgreSqlContainer postgres, WebApplicationFactory<Program> factory)
    {
        this.postgres = postgres;
        this.factory = factory;
        Client = factory.CreateClient();
    }

    public HttpClient Client { get; }

    public static async Task<ControlPlaneFixture> StartAsync(bool isPlatformAdmin)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(controlPlane, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        await using (var context = new ControlPlaneDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            if (isPlatformAdmin)
            {
                context.PlatformAdmins.Add(PlatformAdmin.Create(
                    Guid.NewGuid(),
                    ExternalUserId.Create(TestUserId),
                    DateTimeOffset.UtcNow));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ControlPlane", controlPlane);
            builder.UseSetting("ConnectionStrings:ControlPlaneRead", controlPlane);
            builder.UseSetting("ConnectionStrings:TenantData", connectionString);
            builder.ConfigureTestServices(services =>
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { }));
        });

        return new ControlPlaneFixture(postgres, factory);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", TestUserId)], SchemeName);
            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası veya 404 — endpoint henüz yok.

- [ ] **Step 4: Yetkilendirme gereksinimini ve handler'ını yaz**

`apps/api/src/ControlPlane/Authorization/PlatformAdminRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ControlPlane.Authorization;

public sealed class PlatformAdminRequirement : IAuthorizationRequirement;
```

`apps/api/src/ControlPlane/Authorization/PlatformAdminHandler.cs`:

```csharp
using System.Security.Claims;
using Domain.Access;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Authorization;

public sealed class PlatformAdminHandler(ControlPlaneDbContext dbContext)
    : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        var externalUserId = context.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            return;
        }

        var userId = ExternalUserId.Create(externalUserId);
        var isPlatformAdmin = await dbContext.PlatformAdmins
            .AsNoTracking()
            .AnyAsync(admin => admin.ExternalUserId == userId);

        if (isPlatformAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
```

- [ ] **Step 5: Endpoint'leri ve kayıt uzantılarını yaz**

`apps/api/src/ControlPlane/ControlPlaneEndpoints.cs`:

```csharp
using ControlPlane.Authorization;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane;

public static class ControlPlaneEndpoints
{
    public const string PolicyName = "RequirePlatformAdmin";

    public static IServiceCollection AddControlPlane(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PlatformAdminHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy.AddRequirements(new PlatformAdminRequirement()));

        return services;
    }

    public static IEndpointRouteBuilder MapControlPlane(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api/v1").RequireAuthorization(PolicyName);

        group.MapGet("/tenants", async (ControlPlaneDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var tenants = await dbContext.Tenants
                .AsNoTracking()
                .OrderByDescending(tenant => tenant.CreatedAt)
                .Select(tenant => new
                {
                    id = tenant.Id,
                    alias = tenant.Alias.Value,
                    status = tenant.Status.ToString(),
                    createdAt = tenant.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(tenants);
        });

        return endpoints;
    }
}
```

- [ ] **Step 6: `Program.cs`'e bağla**

`builder.Services.AddAuthorization();` satırını `builder.Services.AddControlPlane();` ile değiştir ve `app.MapOpenApi();` satırından sonra `app.MapControlPlane();` ekle.

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Her iki yetkilendirme testi de PASS.

- [ ] **Step 8: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): separate the control plane surface behind a platform admin policy"
```

---

### Görev 9: `src/Migrator` projesi

**Files:**
- Create: `apps/api/src/Migrator/Migrator.csproj`
- Create: `apps/api/src/Migrator/Program.cs`
- Create: `apps/api/src/Infrastructure/Persistence/Tenants/TenantSchemaMigrator.cs`
- Delete: `apps/api/src/Infrastructure/Persistence/Tenants/PostgresTenantDatabaseMigrator.cs`
- Delete: `apps/api/src/Infrastructure/Persistence/Tenants/PostgresTenantDatabaseProvisioner.cs`
- Create: `apps/api/src/Infrastructure/Persistence/Tenants/TenantMigrationRunner.cs`
- Modify: `apps/api/Api.slnx`, `apps/api/AGENTS.md`
- Test: `apps/api/tests/IntegrationTests/TenantSchemaMigratorTests.cs`
- Test: `apps/api/tests/IntegrationTests/TenantMigrationRunnerTests.cs`

**Interfaces:**
- Consumes: Görev 3'ten `ControlPlaneDbContext`, `TenantDbContextFactory.Create(string databaseName)`.
- Produces: `TenantSchemaMigrator.MigrateAsync(string databaseName, CancellationToken)`; `migrate control-plane` ve `migrate tenants [--tenant <alias>]` komutları.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/TenantSchemaMigratorTests.cs`:

```csharp
using Infrastructure.Persistence.Tenants;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantSchemaMigratorTests
{
    [Fact]
    public async Task MigrateAsync_ForAnExistingDatabase_CreatesTheTenantSchema()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        // Act
        await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await HasUsersTableAsync(connectionString, "tenant_acme"));
    }

    [Fact]
    public async Task MigrateAsync_ForAMissingDatabase_ThrowsAndDoesNotCreateIt()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        // Act
        var act = async () => await migrator.MigrateAsync("tenant_missing", TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.False(await DatabaseExistsAsync(connectionString, "tenant_missing"));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> HasUsersTableAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is "users";
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `TenantSchemaMigrator` bulunamıyor.

- [ ] **Step 3: `TenantSchemaMigrator`'ı yaz**

`apps/api/src/Infrastructure/Persistence/Tenants/TenantSchemaMigrator.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantSchemaMigrator(TenantDbContextFactory contextFactory)
{
    public async Task MigrateAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(databaseName);

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            throw new InvalidOperationException(
                $"Tenant database '{databaseName}' does not exist. Provisioning creates tenant databases; migration never does.",
                exception);
        }
    }
}
```

- [ ] **Step 4: Eski migrator ve provisioner'ı sil**

Tenant veritabanı yaratma işi Spec 2'ye (provisioning) aittir; bu tipler ve testleri kalkar. Veri izolasyonu testi Görev 11'de sıfırdan yazılacağı için dosyanın tamamı silinir.

```bash
cd apps/api
rm src/Infrastructure/Persistence/Tenants/PostgresTenantDatabaseMigrator.cs
rm src/Infrastructure/Persistence/Tenants/PostgresTenantDatabaseProvisioner.cs
rm tests/IntegrationTests/TenantDatabaseProvisionerTests.cs
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `TenantSchemaMigratorTests` PASS.

- [ ] **Step 6: Status filtresi için başarısız testi yaz**

Migrator'ın hangi tenant'ları migrate edeceği karar mantığıdır ve test edilebilmelidir; bu yüzden konsol projesinin `Program.cs`'ine değil, Infrastructure'daki bir sınıfa konur.

`apps/api/tests/IntegrationTests/TenantMigrationRunnerTests.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantMigrationRunnerTests
{
    [Fact]
    public async Task RunAsync_MigratesActiveTenantsAndSkipsTheRest()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using var controlPlaneDbContext = await CreateControlPlaneAsync(connectionString);
        var active = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
        active.ChangeStatus(TenantStatus.Active, DateTimeOffset.UtcNow);
        var provisioning = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("globex"), DateTimeOffset.UtcNow);
        controlPlaneDbContext.Tenants.AddRange(active, provisioning);
        await controlPlaneDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, $"CREATE DATABASE {active.DatabaseName.Value}");
        var factory = new TenantDbContextFactory(connectionString);
        var runner = new TenantMigrationRunner(controlPlaneDbContext, new TenantSchemaMigrator(factory));

        // Act
        var outcomes = await runner.RunAsync(null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("migrated", Assert.Single(outcomes, outcome => outcome.Alias == "acme").Result);
        Assert.Equal("skipped", Assert.Single(outcomes, outcome => outcome.Alias == "globex").Result);
        Assert.False(await DatabaseExistsAsync(connectionString, provisioning.DatabaseName.Value));
    }

    [Fact]
    public async Task RunAsync_ForAnActiveTenantWithoutADatabase_ReportsFailure()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using var controlPlaneDbContext = await CreateControlPlaneAsync(connectionString);
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
        tenant.ChangeStatus(TenantStatus.Active, DateTimeOffset.UtcNow);
        controlPlaneDbContext.Tenants.Add(tenant);
        await controlPlaneDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var runner = new TenantMigrationRunner(
            controlPlaneDbContext,
            new TenantSchemaMigrator(new TenantDbContextFactory(connectionString)));

        // Act
        var outcomes = await runner.RunAsync(null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("failed", Assert.Single(outcomes).Result);
        Assert.False(await DatabaseExistsAsync(connectionString, tenant.DatabaseName.Value));
    }

    private static async Task<ControlPlaneDbContext> CreateControlPlaneAsync(string connectionString)
    {
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "control_plane" };
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(builder.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        var context = new ControlPlaneDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return context;
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 7: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `TenantMigrationRunner` bulunamıyor.

- [ ] **Step 8: `TenantMigrationRunner`'ı yaz**

`apps/api/src/Infrastructure/Persistence/Tenants/TenantMigrationRunner.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Tenants;

public sealed record TenantMigrationOutcome(string Alias, string Result, string? Error = null);

public sealed class TenantMigrationRunner(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantSchemaMigrator schemaMigrator)
{
    public async Task<IReadOnlyList<TenantMigrationOutcome>> RunAsync(
        string? alias,
        CancellationToken cancellationToken)
    {
        var query = controlPlaneDbContext.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(alias))
        {
            var tenantAlias = TenantAlias.Create(alias);
            query = query.Where(tenant => tenant.Alias == tenantAlias);
        }

        var tenants = await query.OrderBy(tenant => tenant.CreatedAt).ToListAsync(cancellationToken);
        var outcomes = new List<TenantMigrationOutcome>();

        foreach (var tenant in tenants)
        {
            if (tenant.Status is not (TenantStatus.Active or TenantStatus.Suspended))
            {
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, "skipped"));
                continue;
            }

            try
            {
                await schemaMigrator.MigrateAsync(tenant.DatabaseName.Value, cancellationToken);
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, "migrated"));
            }
            catch (Exception exception)
            {
                outcomes.Add(new TenantMigrationOutcome(tenant.Alias.Value, "failed", exception.Message));
            }
        }

        return outcomes;
    }
}
```

- [ ] **Step 9: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `TenantMigrationRunnerTests` içindeki her iki test de PASS.

- [ ] **Step 10: Migrator konsol projesini oluştur**

```bash
cd apps/api
dotnet new console -o src/Migrator -n Migrator -f net10.0
dotnet add src/Migrator/Migrator.csproj reference src/Infrastructure/Infrastructure.csproj src/Domain/Domain.csproj
dotnet add src/Migrator/Migrator.csproj package Microsoft.Extensions.Configuration.Json
dotnet add src/Migrator/Migrator.csproj package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet sln Api.slnx add src/Migrator/Migrator.csproj
```

- [ ] **Step 11: Migrator'ı yaz**

Konsol projesi yalnızca yapılandırmayı okur, komutu çözer ve sonucu yazdırır; karar mantığı `TenantMigrationRunner`'dadır.

`apps/api/src/Migrator/Program.cs`:

```csharp
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
    .UseNpgsql(
        Required("ControlPlane"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
    .Options;

return args switch
{
    ["migrate", "control-plane", ..] => await MigrateControlPlaneAsync(),
    ["migrate", "tenants", ..] => await MigrateTenantsAsync(configuration["tenant"]),
    _ => Usage()
};

string Required(string name) =>
    configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");

async Task<int> MigrateControlPlaneAsync()
{
    await using var context = new ControlPlaneDbContext(options);
    await context.Database.MigrateAsync();
    Console.WriteLine("control plane: migrated");

    return 0;
}

async Task<int> MigrateTenantsAsync(string? alias)
{
    await using var context = new ControlPlaneDbContext(options);
    var runner = new TenantMigrationRunner(
        context,
        new TenantSchemaMigrator(new TenantDbContextFactory(Required("TenantData"))));
    var outcomes = await runner.RunAsync(alias, CancellationToken.None);

    foreach (var outcome in outcomes)
    {
        if (outcome.Result == "failed")
        {
            Console.Error.WriteLine($"{outcome.Alias}: FAILED - {outcome.Error}");
        }
        else
        {
            Console.WriteLine($"{outcome.Alias}: {outcome.Result}");
        }
    }

    return outcomes.Any(outcome => outcome.Result == "failed") ? 1 : 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: migrate control-plane | migrate tenants [--tenant <alias>]");

    return 2;
}
```

- [ ] **Step 12: Migrator'ı lokal veritabanına karşı çalıştır**

```bash
cd apps/api
dotnet run --project src/Migrator -- migrate control-plane \
  --ConnectionStrings:ControlPlane "Host=localhost;Port=5432;Database=control_plane;Username=st_migrator;Password=dev_migrator" \
  --ConnectionStrings:TenantData "Host=localhost;Port=5432;Username=st_migrator;Password=dev_migrator"
```

Expected: `control plane: migrated`

- [ ] **Step 13: Grant'ları uygula ve tenant listesinin boş olduğunu doğrula**

```bash
cd apps/api
docker exec -i st-postgres psql -U postgres -d control_plane -f - < scripts/grant-control-plane.sql
dotnet run --project src/Migrator -- migrate tenants \
  --ConnectionStrings:ControlPlane "Host=localhost;Port=5432;Database=control_plane;Username=st_migrator;Password=dev_migrator" \
  --ConnectionStrings:TenantData "Host=localhost;Port=5432;Username=st_migrator;Password=dev_migrator"
```

Expected: Çıktı yok ve exit code 0 — henüz tenant yok, komut hiçbir şey yapmaz.

- [ ] **Step 14: `AGENTS.md` komutlarını güncelle**

`apps/api/AGENTS.md` içindeki eski migration komutunu şu satırlarla değiştir:

```markdown
- Add control plane migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations`
- Add tenant migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations`
- Migrate control plane: `dotnet run --project src/Migrator -- migrate control-plane`
- Migrate existing tenants: `dotnet run --project src/Migrator -- migrate tenants`
```

Kurallar listesine ekle:

```markdown
- Run migrations as a deploy step through `src/Migrator`, never at application startup. The migrator updates existing tenant databases and fails when one is missing; creating tenant databases belongs to provisioning.
```

- [ ] **Step 15: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): migrate schemas from a dedicated migrator project"
```

---

### Görev 10: Sistem rol ve permission seed'i

**Files:**
- Modify: `apps/api/src/Infrastructure/Persistence/Tenants/Configurations/TenantPermissionConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/Tenants/Configurations/TenantRoleConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/Tenants/Configurations/TenantRolePermissionConfiguration.cs`
- Test: `apps/api/tests/IntegrationTests/TenantSchemaMigratorTests.cs`

**Interfaces:**
- Consumes: `Domain.Access.SystemAccessCatalog.Permissions` ve `.Roles`.
- Produces: Migrate edilmiş her tenant veritabanında dolu `permissions`, `roles` ve `role_permissions` tabloları.

- [ ] **Step 1: Başarısız testi yaz**

`TenantSchemaMigratorTests` sınıfına ekle:

```csharp
[Fact]
public async Task MigrateAsync_SeedsTheSystemAccessCatalog()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var connectionString = postgres.GetConnectionString();
    await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
    var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

    // Act
    await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);

    // Assert
    var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "tenant_acme" };
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand(
        "SELECT (SELECT count(*) FROM permissions), (SELECT count(*) FROM roles), (SELECT count(*) FROM role_permissions)",
        connection);
    await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
    await reader.ReadAsync(TestContext.Current.CancellationToken);

    Assert.Equal(SystemAccessCatalog.Permissions.Count, reader.GetInt64(0));
    Assert.Equal(SystemAccessCatalog.Roles.Count, reader.GetInt64(1));
    Assert.Equal(SystemAccessCatalog.Roles.Sum(role => role.PermissionIds.Count), reader.GetInt64(2));
}
```

Dosyanın başına `using Domain.Access;` ekle.

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: FAIL — sayılar sıfır.

- [ ] **Step 3: `HasData` yapılandırmalarını katalogdan besle**

`TenantPermissionConfiguration.Configure` metodunun sonuna ekle:

```csharp
builder.HasData(SystemAccessCatalog.Permissions.Select(permission => new
{
    permission.Id,
    permission.Code,
    permission.Name,
    permission.Description
}));
```

`TenantRoleConfiguration.Configure` metodunun sonuna ekle:

```csharp
builder.HasData(SystemAccessCatalog.Roles.Select(role => new
{
    role.Id,
    role.Code,
    role.Name,
    role.Description
}));
```

`TenantRolePermissionConfiguration.Configure` metodunun sonuna ekle:

```csharp
builder.HasData(SystemAccessCatalog.Roles.SelectMany(role =>
    role.PermissionIds.Select(permissionId => new { RoleId = role.Id, PermissionId = permissionId })));
```

Her üç dosyaya `using Domain.Access;` ekle.

- [ ] **Step 4: Migration üret**

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add SeedSystemAccessCatalog \
  --project src/Infrastructure --startup-project src/Api \
  --context TenantDbContext --output-dir Persistence/Tenants/Migrations
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `MigrateAsync_SeedsTheSystemAccessCatalog` PASS.

- [ ] **Step 6: Şema yakınsaması testini ekle**

Artık iki tenant migration'ı olduğu için, provisioning yoluyla sıfırdan kurulan bir veritabanı ile migrator yoluyla eski sürümden yükseltilen bir veritabanının aynı yere vardığı kanıtlanabilir. Spec 1'in "aynı adımları nasıl garanti ediyoruz" sorusunun testi budur.

`TenantSchemaMigratorTests` sınıfına ekle:

```csharp
[Fact]
public async Task MigrateAsync_ConvergesRegardlessOfTheStartingVersion()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var connectionString = postgres.GetConnectionString();
    await ExecuteAsync(connectionString, "CREATE DATABASE tenant_upgraded");
    await ExecuteAsync(connectionString, "CREATE DATABASE tenant_fresh");
    var contextFactory = new TenantDbContextFactory(connectionString);

    await using (var stepped = contextFactory.Create("tenant_upgraded"))
    {
        await stepped.GetService<IMigrator>()
            .MigrateAsync("InitialTenantAccess", TestContext.Current.CancellationToken);
    }

    var migrator = new TenantSchemaMigrator(contextFactory);

    // Act
    await migrator.MigrateAsync("tenant_upgraded", TestContext.Current.CancellationToken);
    await migrator.MigrateAsync("tenant_fresh", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(
        await AppliedMigrationsAsync(connectionString, "tenant_fresh"),
        await AppliedMigrationsAsync(connectionString, "tenant_upgraded"));
}

private static async Task<List<string>> AppliedMigrationsAsync(string connectionString, string databaseName)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand(
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
        connection);
    await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
    var migrationIds = new List<string>();

    while (await reader.ReadAsync(TestContext.Current.CancellationToken))
    {
        migrationIds.Add(reader.GetString(0));
    }

    return migrationIds;
}
```

Dosyanın başına ekle:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
```

- [ ] **Step 7: Yakınsama testinin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `MigrateAsync_ConvergesRegardlessOfTheStartingVersion` PASS — iki veritabanının uygulanmış migration listesi aynı.

- [ ] **Step 8: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): seed the system access catalog through tenant migrations"
```

---

### Görev 11: `TenantIsolationTests` projesi ve credential sınırı testi

**Files:**
- Create: `apps/api/tests/TenantIsolationTests/TenantIsolationTests.csproj`
- Create: `apps/api/tests/TenantIsolationTests/TenantDataIsolationTests.cs`
- Create: `apps/api/tests/TenantIsolationTests/CredentialBoundaryTests.cs`
- Modify: `apps/api/Api.slnx`

**Interfaces:**
- Consumes: Görev 4'ün `scripts/bootstrap-roles.sql` ve `scripts/grant-control-plane.sql` dosyaları; Görev 9'un `TenantSchemaMigrator`'ı.
- Produces: `st_tenant` rolünün control plane'e yazamadığını ve DDL çalıştıramadığını kanıtlayan testler.

- [ ] **Step 1: Test projesini oluştur**

```bash
cd apps/api
dotnet new classlib -o tests/TenantIsolationTests -n TenantIsolationTests -f net10.0
rm tests/TenantIsolationTests/Class1.cs
dotnet add tests/TenantIsolationTests/TenantIsolationTests.csproj package xunit.v3.mtp-v2
dotnet add tests/TenantIsolationTests/TenantIsolationTests.csproj package Testcontainers.PostgreSql
dotnet add tests/TenantIsolationTests/TenantIsolationTests.csproj package Npgsql
dotnet add tests/TenantIsolationTests/TenantIsolationTests.csproj reference src/Infrastructure/Infrastructure.csproj src/Domain/Domain.csproj
dotnet sln Api.slnx add tests/TenantIsolationTests/TenantIsolationTests.csproj
```

`tests/TenantIsolationTests/TenantIsolationTests.csproj` içindeki `PropertyGroup`'a ekle:

```xml
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
```

- [ ] **Step 2: Credential sınırı testini yaz**

`apps/api/tests/TenantIsolationTests/CredentialBoundaryTests.cs`:

```csharp
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TenantIsolationTests;

public sealed class CredentialBoundaryTests
{
    [Fact]
    public async Task TenantRole_CannotWriteToTheControlPlane()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO control.tenants (id, alias, database_name, status, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), 'intruder', 'tenant_intruder', 'Active', now(), now())",
            connection);

        // Act
        var act = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TenantRole_CannotRunSchemaChanges()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("CREATE TABLE control.intruder (id uuid)", connection);

        // Act
        var act = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TenantRole_CanReadTheTenantCatalogue()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM control.tenants", connection);

        // Act
        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, count);
    }

    private static async Task<PostgreSqlContainer> StartControlPlaneAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), await ReadScriptAsync("bootstrap-roles.sql"));

        var controlPlane = WithDatabase(postgres.GetConnectionString(), "control_plane");
        await ExecuteAsync(postgres.GetConnectionString(), "CREATE DATABASE control_plane");

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(controlPlane, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        await using var context = new ControlPlaneDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(controlPlane, await ReadScriptAsync("grant-control-plane.sql"));

        return postgres;
    }

    private static async Task<string> ReadScriptAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", fileName);
        var sql = await File.ReadAllTextAsync(Path.GetFullPath(path), TestContext.Current.CancellationToken);

        return sql
            .Replace(":'migrator_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'provisioner_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'control_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'tenant_password'", "'test'", StringComparison.Ordinal);
    }

    private static string TenantConnectionString(PostgreSqlContainer postgres)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = "control_plane",
            Username = "st_tenant",
            Password = "test"
        };

        return builder.ConnectionString;
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 3: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/TenantIsolationTests/TenantIsolationTests.csproj`
Expected: Üçü de PASS. Bu testler yeni kod değil, Görev 4'teki script'lerin kurduğu sınırı kanıtlar; herhangi biri FAIL ederse `scripts/grant-control-plane.sql` yanlış demektir — `st_tenant`'a yalnızca `GRANT SELECT ON control.tenants, control.memberships` verildiğini doğrula.

- [ ] **Step 4: Veri izolasyonu testini yaz**

`apps/api/tests/TenantIsolationTests/TenantDataIsolationTests.cs`:

```csharp
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TenantIsolationTests;

public sealed class TenantDataIsolationTests
{
    [Fact]
    public async Task TenantData_CannotBeReadFromAnotherTenantDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var contextFactory = new TenantDbContextFactory(connectionString);
        var migrator = new TenantSchemaMigrator(contextFactory);
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_globex");
        await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);
        await migrator.MigrateAsync("tenant_globex", TestContext.Current.CancellationToken);
        await using var acme = contextFactory.Create("tenant_acme");
        await using var globex = contextFactory.Create("tenant_globex");
        await acme.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO users (id, external_user_id, status) VALUES ({Guid.NewGuid()}, {"user_2abc123"}, {"Active"})",
            TestContext.Current.CancellationToken);

        // Act
        var acmeUsers = await acme.Users.ToListAsync(TestContext.Current.CancellationToken);
        var globexUsers = await globex.Users.ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("user_2abc123", Assert.Single(acmeUsers).ExternalUserId.Value);
        Assert.Empty(globexUsers);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 5: Çözümün tamamını çalıştır**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Dört test projesinin tamamı PASS.

- [ ] **Step 6: `AGENTS.md`'yi son haline getir**

Test kuralına ekle:

```markdown
- Keep tenant isolation and credential boundary tests in the `TenantIsolationTests` project.
```

- [ ] **Step 7: Commit**

```bash
git add -A apps/api
git commit -m "test(api): prove the tenant credential boundary in a dedicated project"
```

---

## Tamamlanma Kontrolü

Tüm görevler bittiğinde şunlar doğrulanmalı:

- [ ] `dotnet test --solution apps/api/Api.slnx` — dört proje de yeşil
- [ ] `docker compose down -v && docker compose up -d` sonrası şu sıra çalışıyor: bootstrap-roles → `migrate control-plane` → grant-control-plane → `migrate tenants` (boş) → `dotnet run --project src/Api`
- [ ] `apps/api/AGENTS.md` yeni komutları ve üç yeni kuralı içeriyor
- [ ] `systemdb`, `admin` şeması, `AdminDbContext` ve `PostgresTenantDatabase*` tiplerine hiçbir referans kalmamış: `grep -rn "systemdb\|AdminDbContext\|PostgresTenantDatabase" apps/api --include=*.cs --include=*.md --include=*.json` boş dönüyor
