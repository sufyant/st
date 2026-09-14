# Tenant Başına Veritabanı Credential'ı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** [Spec 7](../specs/2026-09-13-tenant-basina-db-credential-design.md)'yi uygulamak: paylaşılan `st_tenant` rolünü tenant başına dinamik bir Postgres role'üyle değiştirmek, dört sabit rolü `st_` önekinden arındırmak, ve tenant silme akışına outbox'ın `NextAttemptAt`'ini kullanan 30 günlük gecikmeli bir hard-delete eklemek.

**Architecture:** Yeni bir `TenantRoleName` value object'i (deterministik, `TenantDatabaseName` deseniyle birebir aynı) ve `TenantCredential` entity'si (control plane'de, `IDataProtection` ile şifreli parola taşır) eklenir. `TenantProvisioner.GrantTenantAccessAsync` artık paylaşılan role yerine üretilen role'e GRANT verir ve düz metin parolayı çağıran tarafa döndürür — parolayı şifreleyip saklamak `TenantProvisioningHandler`'ın işi. İstek yolunda yeni bir `TenantCredentialResolver` (mevcut `TenantResolver` deseniyle, ayrı sorumluluk) credential'ı çözüp cache'ler; `TenantAccessMiddleware` bunu `TenantContext`'e yazar, `Program.cs`'deki scoped `TenantDbContext` factory'si oradan okur. Tenant silme, `OutboxMessage`'ın zaten var olan `NextAttemptAt` kolonunu 30 gün ileriye ayarlayarak yeni bir scheduler kurmadan çözülür.

**Tech Stack:** .NET 10, EF Core (Npgsql), PostgreSQL, `Microsoft.AspNetCore.DataProtection` (ek paket değil, framework referansı), xUnit v3 (Microsoft.Testing.Platform), Testcontainers, Dapper.

## Global Constraints

- Hedef `net10.0`. `Domain` projesi hiçbir pakete ve hiçbir projeye referans vermez.
- Migration biriktirilmez: `ControlPlaneDbContext`'e yeni bir entity eklendiği için tek `InitialControlPlane` migration'ı silinip yeniden üretilir (Spec 6 karar 16, decision 39'un "ilk kalıcı ortamdan önce" penceresi).
- Her görev şu ikisi yeşilken biter: `dotnet build Api.slnx` ve `dotnet test --solution Api.slnx`.
- Rol isimlerinde `st_` öneki kullanılmaz: `migrator`, `provisioner`, `control`, `resolver`. Tenant başına dinamik rol: `access_<tenant-id:N>`.
- AAA yorum etiketleri (`// Arrange // Act // Assert`) bu projenin no-comment kuralının tek istisnasıdır — her testte kullanılır.
- Her görev kendi commit'iyle biter, mesaj formatı `type(scope): açıklama`.

---

### Task 1: Bootstrap rollerini yeniden adlandır

**Files:**
- Modify: `apps/api/scripts/bootstrap-roles.sql`
- Modify: `apps/api/scripts/grant-control-plane.sql`
- Modify: `apps/api/tests/TenantIsolationTests/CredentialBoundaryTests.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantProvisionerTests.cs`
- Modify: `apps/api/tests/IntegrationTests/ProvisioningFixture.cs`

**Interfaces:**
- Produces: SQL script'lerdeki rol adları (`migrator`, `provisioner`, `control`, `resolver`) — sonraki tüm görevler bunları kullanır.

- [x] **Step 1: Script'leri güncelle**

`apps/api/scripts/bootstrap-roles.sql`:
```sql
-- Creates the four login roles. Requires a superuser connection and runs once per environment.
-- Passwords are supplied as psql variables so that no secret is stored in the repository:
--   psql -v migrator_password=... -v provisioner_password=... \
--        -v control_password=... -v resolver_password=... -f scripts/bootstrap-roles.sql

CREATE ROLE migrator LOGIN PASSWORD :'migrator_password' CREATEDB;
CREATE ROLE provisioner LOGIN PASSWORD :'provisioner_password' CREATEDB CREATEROLE;
CREATE ROLE control LOGIN PASSWORD :'control_password';
CREATE ROLE resolver LOGIN PASSWORD :'resolver_password';

-- Provisioning owns the tenant tables it creates, and the deploy-time migrator alters them,
-- so the migrator must be able to act as their owner.
GRANT provisioner TO migrator;
```

`apps/api/scripts/grant-control-plane.sql`:
```sql
-- Grants control plane access to the application roles.
-- Run against the control_plane database after `migrate control-plane` has succeeded.

GRANT CONNECT ON DATABASE control_plane TO control, resolver;
GRANT USAGE ON SCHEMA control TO control, resolver;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA control TO control;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA control
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO control;

GRANT SELECT ON control.tenants, control.memberships TO resolver;
```

- [x] **Step 2: Testlerdeki eski isimleri güncelle**

`CredentialBoundaryTests.cs`: `ReadScriptAsync`'teki dört `.Replace` çağrısını yeni placeholder adlarına çevir (`:'migrator_password'` vb. aynı kalır, sadece rol adları script içinde değişti — dosya zaten script'i okuyup şifreleri enjekte ediyor, ekstra değişiklik gerekmez). `TenantConnectionString`'de `Username = "st_tenant"` → `Username = "resolver"`.

`TenantProvisionerTests.cs`: `GrantTenantAccessAsync_RunTwice_LetsTheTenantRoleReadTheTables` testinde `"CREATE ROLE st_tenant LOGIN PASSWORD 'test'"` → bu test Task 5'te tamamen değişecek (yeni imza), burada sadece derlenir halde tutmak için `st_tenant` → `resolver` yap.

`ProvisioningFixture.cs`: `"CREATE ROLE st_tenant LOGIN PASSWORD 'test'"` → `"CREATE ROLE resolver LOGIN PASSWORD 'test'"`.

- [x] **Step 3: Testleri çalıştır**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~CredentialBoundaryTests|FullyQualifiedName~TenantProvisionerTests"`
Expected: PASS (henüz tenant-başına-rol yok, bu testler hâlâ paylaşılan `resolver`/eski davranışı doğruluyor)

- [x] **Step 4: Commit**

```bash
git add apps/api/scripts/bootstrap-roles.sql apps/api/scripts/grant-control-plane.sql apps/api/tests/TenantIsolationTests/CredentialBoundaryTests.cs apps/api/tests/IntegrationTests/TenantProvisionerTests.cs apps/api/tests/IntegrationTests/ProvisioningFixture.cs
git commit -m "refactor(security): drop the st_ prefix from bootstrap role names"
```

---

### Task 2: `TenantRoleName` value object'i

**Files:**
- Create: `apps/api/src/Domain/ControlPlane/Tenants/TenantRoleName.cs`
- Test: `apps/api/tests/UnitTests/ControlPlaneTenants/TenantRoleNameTests.cs`

**Interfaces:**
- Produces: `TenantRoleName.ForTenant(TenantId) -> TenantRoleName`, `TenantRoleName.Create(string) -> TenantRoleName`, `TenantRoleName.Value -> string`. Task 3, 5, 6, 7'nin hepsi bunu kullanır.

- [x] **Step 1: Başarısız testi yaz**

```csharp
using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantRoleNameTests
{
    [Fact]
    public void ForTenant_ProducesAnAccessPrefixedLowercaseIdentifier()
    {
        // Arrange
        var tenantId = TenantId.From(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"));

        // Act
        var roleName = TenantRoleName.ForTenant(tenantId);

        // Assert
        Assert.Equal("access_018f4e3b7c9d4a1ba2c3d4e5f6a7b8c9", roleName.Value);
    }

    [Fact]
    public void Create_RejectsUppercase()
    {
        // Arrange & Act
        var act = () => TenantRoleName.Create("Access_Bad");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_RejectsEmpty()
    {
        // Arrange & Act
        var act = () => TenantRoleName.Create("");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_AcceptsAValidIdentifier()
    {
        // Arrange & Act
        var roleName = TenantRoleName.Create("access_abc123");

        // Assert
        Assert.Equal("access_abc123", roleName.Value);
    }
}
```

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantRoleNameTests"`
Expected: FAIL (derleme hatası — `TenantRoleName` yok)

- [x] **Step 3: `TenantRoleName` yaz**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Domain.ControlPlane.Tenants;

public sealed record TenantRoleName
{
    private const int MaximumByteLength = 63;

    private static readonly Regex IdentifierPattern = new(
        "\\A[a-z][a-z0-9_]*\\z",
        RegexOptions.CultureInvariant);

    public string Value { get; }

    private TenantRoleName(string value)
    {
        Value = value;
    }

    public static TenantRoleName ForTenant(TenantId tenantId) =>
        new($"access_{tenantId.Value:N}");

    public static TenantRoleName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !IdentifierPattern.IsMatch(value) ||
            Encoding.UTF8.GetByteCount(value) > MaximumByteLength)
        {
            throw new ArgumentException(
                "Tenant role name must be a lowercase PostgreSQL identifier of at most 63 bytes.",
                nameof(value));
        }

        return new TenantRoleName(value);
    }
}
```

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantRoleNameTests"`
Expected: PASS (4 test)

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Domain/ControlPlane/Tenants/TenantRoleName.cs apps/api/tests/UnitTests/ControlPlaneTenants/TenantRoleNameTests.cs
git commit -m "feat(domain): add TenantRoleName value object"
```

---

### Task 3: `TenantCredential` entity'si

**Files:**
- Create: `apps/api/src/Domain/ControlPlane/Tenants/TenantCredential.cs`
- Test: `apps/api/tests/UnitTests/ControlPlaneTenants/TenantCredentialTests.cs`

**Interfaces:**
- Consumes: `TenantId` ([Domain/ControlPlane/Tenants/Tenant.cs](../../src/Domain/ControlPlane/Tenants/Tenant.cs)), `TenantRoleName` (Task 2), `Entity<TId>`/`IAuditable` ([Domain/Shared](../../src/Domain/Shared)).
- Produces: `TenantCredential.Create(TenantId, TenantRoleName, string encryptedPassword) -> TenantCredential`, `.Rotate(string encryptedPassword)`, `.TenantId`, `.RoleName`, `.EncryptedPassword`. Task 4 (EF config), Task 7 (handler) bunu kullanır.

- [x] **Step 1: Başarısız testi yaz**

```csharp
using Domain.ControlPlane.Tenants;
using Xunit;

namespace UnitTests.ControlPlaneTenants;

public sealed class TenantCredentialTests
{
    private static readonly TenantId TenantId = TenantId.New();

    private static readonly TenantRoleName RoleName = TenantRoleName.ForTenant(TenantId);

    [Fact]
    public void Create_SetsTheGivenFields()
    {
        // Arrange & Act
        var credential = TenantCredential.Create(TenantId, RoleName, "cipher-text");

        // Assert
        Assert.Equal(TenantId, credential.TenantId);
        Assert.Equal(RoleName, credential.RoleName);
        Assert.Equal("cipher-text", credential.EncryptedPassword);
    }

    [Fact]
    public void Create_RejectsAnEmptyEncryptedPassword()
    {
        // Arrange & Act
        var act = () => TenantCredential.Create(TenantId, RoleName, "");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Rotate_ReplacesTheEncryptedPassword()
    {
        // Arrange
        var credential = TenantCredential.Create(TenantId, RoleName, "old-cipher-text");

        // Act
        credential.Rotate("new-cipher-text");

        // Assert
        Assert.Equal("new-cipher-text", credential.EncryptedPassword);
    }
}
```

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantCredentialTests"`
Expected: FAIL (derleme hatası)

- [x] **Step 3: `TenantCredential` yaz**

```csharp
using Domain.Shared;

namespace Domain.ControlPlane.Tenants;

public readonly record struct TenantCredentialId(Guid Value)
{
    public static TenantCredentialId New() => new(Guid.CreateVersion7());

    public static TenantCredentialId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant credential ID cannot be empty.", nameof(value))
        : new TenantCredentialId(value);
}

public sealed class TenantCredential : Entity<TenantCredentialId>, IAuditable
{
    public const string ProtectionPurpose = "Domain.ControlPlane.Tenants.TenantCredential";

    public TenantId TenantId { get; private set; }

    public TenantRoleName RoleName { get; private set; } = null!;

    public string EncryptedPassword { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantCredential()
    {
    }

    public static TenantCredential Create(TenantId tenantId, TenantRoleName roleName, string encryptedPassword)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);

        return new TenantCredential
        {
            Id = TenantCredentialId.New(),
            TenantId = tenantId,
            RoleName = roleName,
            EncryptedPassword = encryptedPassword
        };
    }

    public void Rotate(string encryptedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);

        EncryptedPassword = encryptedPassword;
    }
}
```

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantCredentialTests"`
Expected: PASS (3 test)

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Domain/ControlPlane/Tenants/TenantCredential.cs apps/api/tests/UnitTests/ControlPlaneTenants/TenantCredentialTests.cs
git commit -m "feat(domain): add TenantCredential entity"
```

---

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

### Task 5: `TenantProvisioner.GrantTenantAccessAsync` — tenant başına dinamik rol

**Files:**
- Modify: `apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantProvisionerTests.cs`

**Interfaces:**
- Consumes: `TenantRoleName` (Task 2).
- Produces: `TenantProvisioner.GrantTenantAccessAsync(TenantDatabaseName, TenantRoleName, CancellationToken) -> Task<string>` (döndürülen değer düz metin parola). İmza değişti — eski `GrantTenantAccessAsync(TenantDatabaseName, CancellationToken)` kaldırıldı. Task 7 bu yeni imzayı kullanır.

- [x] **Step 1: Başarısız testi yaz**

`TenantProvisionerTests.cs`'deki `GrantTenantAccessAsync_RunTwice_LetsTheTenantRoleReadTheTables` testini değiştir (artık sabit `st_tenant`/`resolver` role'ü yaratmaya gerek yok, metod kendi rolünü yaratıyor):

```csharp
[Fact]
public async Task GrantTenantAccessAsync_CreatesADedicatedRoleThatCanReadTheTables()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
    var roleName = TenantRoleName.Create("access_acme");
    await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
    await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);

    // Act
    var password = await provisioner.GrantTenantAccessAsync(
        DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Assert
    Assert.False(string.IsNullOrWhiteSpace(password));
    var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
    {
        Database = DatabaseName.Value,
        Username = roleName.Value,
        Password = password
    }.ConnectionString;
    await using var connection = new NpgsqlConnection(tenantConnection);
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
    Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
}

[Fact]
public async Task GrantTenantAccessAsync_RunTwice_RotatesThePasswordAndStillGrantsAccess()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
    var roleName = TenantRoleName.Create("access_acme");
    await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
    await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);
    var firstPassword = await provisioner.GrantTenantAccessAsync(
        DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Act
    var secondPassword = await provisioner.GrantTenantAccessAsync(
        DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Assert
    Assert.NotEqual(firstPassword, secondPassword);
    var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
    {
        Database = DatabaseName.Value,
        Username = roleName.Value,
        Password = secondPassword
    }.ConnectionString;
    await using var connection = new NpgsqlConnection(tenantConnection);
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
    Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
}
```

Bu iki test eskisinin (`GrantTenantAccessAsync_RunTwice_LetsTheTenantRoleReadTheTables`) yerini alır — o testi sil.

Test dosyasının başına `using Domain.ControlPlane.Tenants;` zaten var (kontrol et, `DatabaseName` zaten oradan geliyor).

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisionerTests"`
Expected: FAIL (derleme hatası — imza uyuşmuyor)

- [x] **Step 3: `GrantTenantAccessAsync`'i yeniden yaz**

`TenantProvisioner.cs`'de `TenantRole` sabitini kaldır, `GrantTenantAccessAsync`'i değiştir:

```csharp
public async Task<string> GrantTenantAccessAsync(
    TenantDatabaseName databaseName,
    TenantRoleName roleName,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(databaseName);
    ArgumentNullException.ThrowIfNull(roleName);

    var password = GeneratePassword();

    await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
    await maintenance.OpenAsync(cancellationToken);
    await using var existsCommand = new NpgsqlCommand(
        "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)",
        maintenance);
    existsCommand.Parameters.AddWithValue("name", roleName.Value);
    var roleExists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!;

    await using var roleCommand = new NpgsqlCommand(
        roleExists
            ? $"ALTER ROLE \"{roleName.Value}\" PASSWORD '{password}'"
            : $"CREATE ROLE \"{roleName.Value}\" LOGIN PASSWORD '{password}'",
        maintenance);
    await roleCommand.ExecuteNonQueryAsync(cancellationToken);

    await using var connectCommand = new NpgsqlCommand(
        $"GRANT CONNECT ON DATABASE \"{databaseName.Value}\" TO \"{roleName.Value}\"",
        maintenance);
    await connectCommand.ExecuteNonQueryAsync(cancellationToken);

    await using var tenant = new NpgsqlConnection(TenantConnectionString(databaseName));
    await tenant.OpenAsync(cancellationToken);
    await using var grantCommand = new NpgsqlCommand(
        $"""
        GRANT USAGE ON SCHEMA public TO "{roleName.Value}";
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{roleName.Value}";
        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{roleName.Value}";
        """,
        tenant);
    await grantCommand.ExecuteNonQueryAsync(cancellationToken);

    return password;
}

private static string GeneratePassword() =>
    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
```

`using Domain.ControlPlane.Tenants;` zaten dosyanın başında var (`TenantDatabaseName` için) — `TenantRoleName` aynı namespace'te, ek using gerekmez.

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisionerTests"`
Expected: PASS

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs apps/api/tests/IntegrationTests/TenantProvisionerTests.cs
git commit -m "feat(provisioning): grant tenant access through a dedicated per-tenant role"
```

---

### Task 6: `TenantProvisioner.DropTenantAsync`

**Files:**
- Modify: `apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantProvisionerTests.cs`

**Interfaces:**
- Produces: `TenantProvisioner.DropTenantAsync(TenantDatabaseName, TenantRoleName, CancellationToken) -> Task`. Task 10 (hard-delete handler) bunu kullanır.

- [x] **Step 1: Başarısız testi yaz**

`TenantProvisionerTests.cs`'e ekle:
```csharp
[Fact]
public async Task DropTenantAsync_RemovesTheDatabaseAndTheRole()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
    var roleName = TenantRoleName.Create("access_acme");
    await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
    await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);
    await provisioner.GrantTenantAccessAsync(DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Act
    await provisioner.DropTenantAsync(DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Assert
    Assert.False(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    Assert.False(await RoleExistsAsync(postgres.GetConnectionString(), roleName.Value));
}

[Fact]
public async Task DropTenantAsync_RunTwice_Succeeds()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var provisioner = new TenantProvisioner(postgres.GetConnectionString(), AuditInterceptor);
    var roleName = TenantRoleName.Create("access_acme");
    await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
    await provisioner.GrantTenantAccessAsync(DatabaseName, roleName, TestContext.Current.CancellationToken);
    await provisioner.DropTenantAsync(DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Act
    var act = async () =>
        await provisioner.DropTenantAsync(DatabaseName, roleName, TestContext.Current.CancellationToken);

    // Assert
    await act(); // does not throw
}
```

Yardımcı metod ekle:
```csharp
private static async Task<bool> RoleExistsAsync(string connectionString, string roleName)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand(
        "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)", connection);
    command.Parameters.AddWithValue("name", roleName);

    return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
}
```

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisionerTests"`
Expected: FAIL (derleme hatası — `DropTenantAsync` yok)

- [x] **Step 3: `DropTenantAsync`'i yaz**

`TenantProvisioner.cs`'e ekle:
```csharp
public async Task DropTenantAsync(
    TenantDatabaseName databaseName,
    TenantRoleName roleName,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(databaseName);
    ArgumentNullException.ThrowIfNull(roleName);

    await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
    await maintenance.OpenAsync(cancellationToken);
    await using var dropDatabaseCommand = new NpgsqlCommand(
        $"DROP DATABASE IF EXISTS \"{databaseName.Value}\" WITH (FORCE)",
        maintenance);
    await dropDatabaseCommand.ExecuteNonQueryAsync(cancellationToken);

    await using var dropRoleCommand = new NpgsqlCommand(
        $"DROP ROLE IF EXISTS \"{roleName.Value}\"",
        maintenance);
    await dropRoleCommand.ExecuteNonQueryAsync(cancellationToken);
}
```

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisionerTests"`
Expected: PASS

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs apps/api/tests/IntegrationTests/TenantProvisionerTests.cs
git commit -m "feat(provisioning): add DropTenantAsync for hard-delete"
```

---

### Task 7: `DataProtection` kaydı ve `TenantProvisioningHandler`'ın credential'ı saklaması

**Files:**
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Application/Features/Provisioning/TenantProvisioningHandler.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantProvisioningHandlerTests.cs`
- Modify: `apps/api/tests/IntegrationTests/ProvisioningFixture.cs`

**Interfaces:**
- Consumes: `TenantCredential` (Task 3), `TenantProvisioner.GrantTenantAccessAsync` yeni imza (Task 5).
- Produces: `TenantProvisioningHandler` artık `IDataProtectionProvider` alıyor; `Purpose` sabiti (`TenantCredential.ProtectionPurpose`) — Task 9'daki `TenantCredentialResolver` AYNI purpose string'i kullanmak zorunda.

- [x] **Step 1: Mevcut handler testini oku ve fixture'a DataProtection ekle**

Önce mevcut `TenantProvisioningHandlerTests.cs` dosyasını oku (bu görev sırasında Read ile aç), handler'ın nasıl construct edildiğini gör. `ProvisioningFixture.cs`'e bir `IDataProtectionProvider` alanı ekle:

```csharp
public IDataProtectionProvider DataProtectionProvider { get; } =
    Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("tests");
```

(Using: `Microsoft.AspNetCore.DataProtection` — `Infrastructure.csproj`'ün zaten `FrameworkReference Include="Microsoft.AspNetCore.App"` referansı var, `IntegrationTests.csproj` da `Api.csproj` üzerinden bunu miras alır; derlenmezse `IntegrationTests.csproj`'e `<FrameworkReference Include="Microsoft.AspNetCore.App" />` ekle.)

- [x] **Step 2: Başarısız testi yaz**

`TenantProvisioningHandlerTests.cs`'e (mevcut testlerin yanına, aynı dosyada) ekle:
```csharp
[Fact]
public async Task HandleAsync_StoresAnEncryptedCredentialForTheTenant()
{
    // Arrange
    await using var fixture = await ProvisioningFixture.StartAsync();
    var tenant = await fixture.AddProvisioningTenantAsync("acme");
    var handler = new TenantProvisioningHandler(
        fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
    var message = new TenantProvisioningRequested(
        tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail);

    // Act
    await handler.HandleAsync(message, TestContext.Current.CancellationToken);

    // Assert
    await using var context = fixture.CreateControlPlane();
    var credential = await context.TenantCredentials.SingleAsync(
        c => c.TenantId == tenant.Id, TestContext.Current.CancellationToken);
    Assert.Equal(TenantRoleName.ForTenant(tenant.Id).Value, credential.RoleName.Value);
    var protector = fixture.DataProtectionProvider.CreateProtector(
        TenantCredential.ProtectionPurpose);
    var plainTextPassword = protector.Unprotect(credential.EncryptedPassword);
    Assert.False(string.IsNullOrWhiteSpace(plainTextPassword));
}
```

`using Domain.ControlPlane.Tenants;` zaten dosyada olmalı, yoksa ekle.

- [x] **Step 3: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisioningHandlerTests"`
Expected: FAIL (derleme hatası — `TenantProvisioningHandler` constructor'ı henüz üç parametre almıyor)

- [x] **Step 4: `AddDataProtection` kaydı ekle**

`PostgresServiceCollectionExtensions.cs`'in `AddTenantPersistence` metoduna ekle (metodun başına, `services.AddSingleton<AuditInterceptor>();` satırından önce):
```csharp
services.AddDataProtection();
```

- [x] **Step 5: `TenantProvisioningHandler`'ı güncelle**

```csharp
using System.Text.Json;
using Domain.Shared;
using Domain.Authorization;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Provisioning;

public sealed class TenantProvisioningHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner,
    IDataProtectionProvider dataProtectionProvider) : IOutboxMessageHandler
{
    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector(TenantCredential.ProtectionPurpose);

    public string MessageType => TenantProvisioningRequested.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        HandleAsync(
            JsonSerializer.Deserialize<TenantProvisioningRequested>(payload)
            ?? throw new InvalidOperationException("The outbox payload is empty."),
            cancellationToken);

    public async Task HandleAsync(TenantProvisioningRequested message, CancellationToken cancellationToken)
    {
        var tenantId = TenantId.From(message.TenantId);
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == tenantId,
                         cancellationToken)
                     ?? throw new InvalidOperationException($"Tenant '{message.TenantId}' does not exist.");

        if (tenant.Status is TenantStatus.Active)
        {
            return;
        }

        var step = TenantProvisioningStep.CreatingDatabase;

        try
        {
            await RecordAsync(tenant, step, cancellationToken);
            await provisioner.CreateDatabaseAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.MigratingSchema;
            await RecordAsync(tenant, step, cancellationToken);
            await provisioner.MigrateSchemaAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.GrantingAccess;
            await RecordAsync(tenant, step, cancellationToken);
            await GrantAccessAsync(tenant, cancellationToken);

            step = TenantProvisioningStep.SeedingOwner;
            await RecordAsync(tenant, step, cancellationToken);
            await SeedOwnerAsync(tenant, message.OwnerExternalUserId, message.OwnerEmail, cancellationToken);

            tenant.CompleteProvisioning();
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            tenant.RecordProvisioningFailure(step, exception.Message);
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private async Task RecordAsync(Tenant tenant, TenantProvisioningStep step, CancellationToken cancellationToken)
    {
        tenant.RecordProvisioningProgress(step);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task GrantAccessAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var roleName = TenantRoleName.ForTenant(tenant.Id);
        var password = await provisioner.GrantTenantAccessAsync(tenant.DatabaseName, roleName, cancellationToken);
        var encryptedPassword = protector.Protect(password);

        var credential = await controlPlaneDbContext.TenantCredentials.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenant.Id, cancellationToken);

        if (credential is null)
        {
            controlPlaneDbContext.TenantCredentials.Add(
                TenantCredential.Create(tenant.Id, roleName, encryptedPassword));
        }
        else
        {
            credential.Rotate(encryptedPassword);
        }

        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedOwnerAsync(
        Tenant tenant,
        string ownerExternalUserId,
        string ownerEmail,
        CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(ownerExternalUserId);

        await using var tenantDbContext = provisioner.CreateTenantDbContext(tenant.DatabaseName);
        var user = await tenantDbContext.Users
            .Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.ExternalUserId == externalUserId, cancellationToken);

        if (user is null)
        {
            var ownerRole = await tenantDbContext.Roles.SingleAsync(
                candidate => candidate.Code == AccessCatalog.OwnerRole.Code,
                cancellationToken);
            user = User.Create(externalUserId, EmailAddress.Create(ownerEmail), UserStatus.Active, ownerRole);
            tenantDbContext.Users.Add(user);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }
        else if (user.Role.Code != AccessCatalog.OwnerRole.Code)
        {
            var ownerRole = await tenantDbContext.Roles.SingleAsync(
                candidate => candidate.Code == AccessCatalog.OwnerRole.Code,
                cancellationToken);
            user.AssignRole(ownerRole);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasMembership = await controlPlaneDbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            controlPlaneDbContext.Memberships.Add(
                Membership.Create(tenant.Id, externalUserId));
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [x] **Step 6: Handler'ı çağıran diğer testleri düzelt**

`OutboxDrainerTests.cs`'teki `CreateDrainer` yardımcı metodu `new TenantProvisioningHandler(fixture.CreateControlPlane(), fixture.Provisioner)` çağırıyor — üçüncü parametreyi ekle: `new TenantProvisioningHandler(fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider)`.

- [x] **Step 7: Testleri çalıştır**

Run: `dotnet test --solution Api.slnx`
Expected: PASS (tüm testler)

- [x] **Step 8: Commit**

```bash
git add apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs apps/api/src/Application/Features/Provisioning/TenantProvisioningHandler.cs apps/api/tests/IntegrationTests/TenantProvisioningHandlerTests.cs apps/api/tests/IntegrationTests/ProvisioningFixture.cs apps/api/tests/IntegrationTests/OutboxDrainerTests.cs
git commit -m "feat(provisioning): encrypt and persist the per-tenant credential"
```

---

### Task 8: `OutboxMessage.CreateDelayed`

**Files:**
- Modify: `apps/api/src/Infrastructure/Messaging/OutboxMessage.cs`
- Modify: `apps/api/tests/IntegrationTests/OutboxMessageTests.cs`

**Interfaces:**
- Produces: `OutboxMessage.CreateDelayed(Guid id, string type, string payload, DateTimeOffset createdAt, DateTimeOffset executeAt) -> OutboxMessage`. Task 11 (deprovision endpoint) bunu kullanır.

- [x] **Step 1: Başarısız testi yaz**

`OutboxMessageTests.cs`'e ekle:
```csharp
[Fact]
public void CreateDelayed_IsNotDueUntilTheGivenTime()
{
    // Arrange & Act
    var message = OutboxMessage.CreateDelayed(
        Guid.NewGuid(), "Type", "{}", Now, Now.AddDays(30));

    // Assert
    Assert.Null(message.ProcessedAt);
    Assert.Equal(0, message.AttemptCount);
    Assert.Equal(Now.AddDays(30), message.NextAttemptAt);
    Assert.Equal(Now, message.CreatedAt);
}
```

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~OutboxMessageTests"`
Expected: FAIL (derleme hatası — `CreateDelayed` yok)

- [x] **Step 3: `CreateDelayed`'i yaz, `Create`'i ona devret**

`OutboxMessage.cs`'de `Create` metodunu değiştir:
```csharp
public static OutboxMessage Create(Guid id, string type, string payload, DateTimeOffset createdAt) =>
    CreateDelayed(id, type, payload, createdAt, createdAt);

public static OutboxMessage CreateDelayed(
    Guid id, string type, string payload, DateTimeOffset createdAt, DateTimeOffset executeAt)
{
    if (id == Guid.Empty)
    {
        throw new ArgumentException("Outbox message ID cannot be empty.", nameof(id));
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(type);
    ArgumentException.ThrowIfNullOrWhiteSpace(payload);

    return new OutboxMessage
    {
        Id = id,
        Type = type,
        Payload = payload,
        CreatedAt = createdAt,
        NextAttemptAt = executeAt
    };
}
```

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~OutboxMessageTests"`
Expected: PASS (5 test — 4 eski + 1 yeni)

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Messaging/OutboxMessage.cs apps/api/tests/IntegrationTests/OutboxMessageTests.cs
git commit -m "feat(messaging): add OutboxMessage.CreateDelayed for scheduled execution"
```

---

### Task 9: `TenantCredentialResolver`

**Files:**
- Create: `apps/api/src/Infrastructure/Tenants/TenantCredentialResolver.cs`
- Test: `apps/api/tests/IntegrationTests/TenantCredentialResolverTests.cs`

**Interfaces:**
- Consumes: `[FromKeyedServices("control-plane-read")] NpgsqlDataSource` (zaten kayıtlı, [PostgresServiceCollectionExtensions.cs](../../src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs)), `IDataProtectionProvider`, `IMemoryCache`, `TenantCredential.ProtectionPurpose` (Task 3) — Task 7'deki handler'la **aynı string olmak zorunda**, aksi halde `Unprotect` patlar.
- Produces: `TenantCredentialResolver.ResolveAsync(Guid tenantId, CancellationToken) -> Task<ResolvedTenantCredential?>`, `.Evict(Guid tenantId)`. `ResolvedTenantCredential(string RoleName, string Password)`. Task 13 (`TenantAccessMiddleware`) bunu kullanır.

- [x] **Step 1: Başarısız testi yaz**

```csharp
using Domain.ControlPlane.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantCredentialResolverTests
{
    [Fact]
    public async Task ResolveAsync_DecryptsTheStoredPassword()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);

        // Act
        var resolved = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal("access_acme", resolved.RoleName);
        Assert.Equal("s3cr3t", resolved.Password);
    }

    [Fact]
    public async Task ResolveAsync_ServesTheSecondCallFromCache()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);
        await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenant_credentials");

        // Act
        var cached = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task Evict_ForcesTheNextCallToReadTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);
        await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenant_credentials");

        // Act
        resolver.Evict(tenantId);
        var resolved = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
    }

    private static async Task SeedCredentialAsync(
        string connectionString, Guid tenantId, string roleName, string encryptedPassword)
    {
        await ExecuteAsync(connectionString, "CREATE SCHEMA control");
        await ExecuteAsync(
            connectionString,
            "CREATE TABLE control.tenant_credentials (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, " +
            "role_name text NOT NULL, encrypted_password text NOT NULL)");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO control.tenant_credentials VALUES (gen_random_uuid(), @tenantId, @roleName, @password)",
            connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("roleName", roleName);
        command.Parameters.AddWithValue("password", encryptedPassword);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantCredentialResolverTests"`
Expected: FAIL (derleme hatası — `TenantCredentialResolver` yok)

- [x] **Step 3: `TenantCredentialResolver`'ı yaz**

```csharp
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Domain.ControlPlane.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tenants;

public sealed record ResolvedTenantCredential(string RoleName, string Password);

public sealed class TenantCredentialResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly NpgsqlDataSource dataSource;
    private readonly IMemoryCache cache;
    private readonly IDataProtector protector;

    public TenantCredentialResolver(
        [FromKeyedServices("control-plane-read")] NpgsqlDataSource dataSource,
        IDataProtectionProvider dataProtectionProvider,
        IMemoryCache cache)
    {
        this.dataSource = dataSource;
        this.cache = cache;
        protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
    }

    public async Task<ResolvedTenantCredential?> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(tenantId);

        if (cache.TryGetValue(cacheKey, out ResolvedTenantCredential? cached))
        {
            return cached;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CredentialRow>(new CommandDefinition(
            "SELECT role_name, encrypted_password FROM control.tenant_credentials WHERE tenant_id = @tenantId",
            new { tenantId },
            cancellationToken: cancellationToken));
        var resolved = row is null
            ? null
            : new ResolvedTenantCredential(row.RoleName, protector.Unprotect(row.EncryptedPassword));

        cache.Set(cacheKey, resolved, CacheLifetime);

        return resolved;
    }

    public void Evict(Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private static string CacheKey(Guid tenantId) => $"tenant-credential:{tenantId:N}";

    private sealed record CredentialRow(string RoleName, string EncryptedPassword);
}
```

`TenantCredential.ProtectionPurpose` (Task 3'te tanımlandı) hem burada hem `TenantProvisioningHandler`'da (Task 7) aynı string'i verir — `Domain` projesine hem `Application` hem `Infrastructure` referans verdiği için (bkz. Global Constraints) ikisi de sorunsuz erişir, key ring uyuşmazlığı riski yok.

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantCredentialResolverTests|FullyQualifiedName~TenantProvisioningHandlerTests"`
Expected: PASS

- [x] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Tenants/TenantCredentialResolver.cs apps/api/tests/IntegrationTests/TenantCredentialResolverTests.cs apps/api/src/Application/Features/Provisioning/TenantProvisioningHandler.cs apps/api/tests/IntegrationTests/TenantProvisioningHandlerTests.cs
git commit -m "feat(tenants): add TenantCredentialResolver for the request path"
```

---

### Task 10: Gecikmeli tenant hard-delete

**Files:**
- Create: `apps/api/src/Infrastructure/Messaging/TenantHardDeleteRequested.cs`
- Create: `apps/api/src/Application/Features/Deprovisioning/TenantHardDeleteHandler.cs`
- Modify: `apps/api/src/Application/DependencyInjection.cs`
- Test: `apps/api/tests/IntegrationTests/TenantHardDeleteHandlerTests.cs`

**Interfaces:**
- Consumes: `TenantProvisioner.DropTenantAsync` (Task 6), `TenantRoleName.ForTenant` (Task 2), `Tenant.MarkDeleted()` (mevcut).
- Produces: `TenantHardDeleteRequested(Guid TenantId)`, `TenantHardDeleteHandler : IOutboxMessageHandler`. Task 11 (endpoint) bunu enqueue eder.

- [x] **Step 1: Mesaj tipini yaz**

```csharp
namespace Infrastructure.Messaging;

public sealed record TenantHardDeleteRequested(Guid TenantId)
{
    public const string MessageType = "TenantHardDeleteRequested";
}
```

- [x] **Step 2: Başarısız testi yaz**

```csharp
using System.Text.Json;
using Application.Features.Deprovisioning;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class TenantHardDeleteHandlerTests
{
    [Fact]
    public async Task HandleAsync_DropsTheDatabaseRoleAndCredentialThenMarksTheTenantDeleted()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var provisioningHandler = new TenantProvisioningHandler(
            fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
        await provisioningHandler.HandleAsync(
            new TenantProvisioningRequested(
                tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);
        await using (var context = fixture.CreateControlPlane())
        {
            var reloaded = await context.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            reloaded.BeginDeprovisioning();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var handler = new TenantHardDeleteHandler(fixture.CreateControlPlane(), fixture.Provisioner);

        // Act
        await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Assert
        await using var assertions = fixture.CreateControlPlane();
        var reloadedTenant = await assertions.Tenants.SingleAsync(
            t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
        Assert.Equal(TenantStatus.Deleted, reloadedTenant.Status);
        Assert.False(await assertions.TenantCredentials.AnyAsync(
            c => c.TenantId == tenant.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_RunTwice_IsIdempotent()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        var provisioningHandler = new TenantProvisioningHandler(
            fixture.CreateControlPlane(), fixture.Provisioner, fixture.DataProtectionProvider);
        await provisioningHandler.HandleAsync(
            new TenantProvisioningRequested(
                tenant.Id.Value, ProvisioningFixture.OwnerExternalUserId, ProvisioningFixture.OwnerEmail),
            TestContext.Current.CancellationToken);
        await using (var context = fixture.CreateControlPlane())
        {
            var reloaded = await context.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            reloaded.BeginDeprovisioning();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var handler = new TenantHardDeleteHandler(fixture.CreateControlPlane(), fixture.Provisioner);
        await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantHardDeleteRequested(tenant.Id.Value), TestContext.Current.CancellationToken);

        // Assert
        await act(); // does not throw
    }
}
```

- [x] **Step 3: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantHardDeleteHandlerTests"`
Expected: FAIL (derleme hatası — `TenantHardDeleteHandler` yok)

- [x] **Step 4: `TenantHardDeleteHandler`'ı yaz**

```csharp
using System.Text.Json;
using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Deprovisioning;

public sealed class TenantHardDeleteHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner) : IOutboxMessageHandler
{
    public string MessageType => TenantHardDeleteRequested.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        HandleAsync(
            JsonSerializer.Deserialize<TenantHardDeleteRequested>(payload)
            ?? throw new InvalidOperationException("The outbox payload is empty."),
            cancellationToken);

    public async Task HandleAsync(TenantHardDeleteRequested message, CancellationToken cancellationToken)
    {
        var tenantId = TenantId.From(message.TenantId);
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == tenantId,
                         cancellationToken)
                     ?? throw new InvalidOperationException($"Tenant '{message.TenantId}' does not exist.");

        if (tenant.Status is TenantStatus.Deleted)
        {
            return;
        }

        var roleName = TenantRoleName.ForTenant(tenant.Id);
        await provisioner.DropTenantAsync(tenant.DatabaseName, roleName, cancellationToken);

        var credential = await controlPlaneDbContext.TenantCredentials.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenant.Id, cancellationToken);

        if (credential is not null)
        {
            controlPlaneDbContext.TenantCredentials.Remove(credential);
        }

        tenant.MarkDeleted();
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [x] **Step 5: DI'a kaydet**

`Application/DependencyInjection.cs`'e ekle (`TenantProvisioningHandler` kaydının hemen altına):
```csharp
services.AddScoped<Application.Features.Deprovisioning.TenantHardDeleteHandler>();
services.AddScoped<IOutboxMessageHandler>(provider =>
    provider.GetRequiredService<Application.Features.Deprovisioning.TenantHardDeleteHandler>());
```
(Dosyanın başına `using Application.Features.Deprovisioning;` ekleyip tam nitelikli adları kısaltabilirsin.)

- [x] **Step 6: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantHardDeleteHandlerTests"`
Expected: PASS

- [x] **Step 7: Commit**

```bash
git add apps/api/src/Infrastructure/Messaging/TenantHardDeleteRequested.cs apps/api/src/Application/Features/Deprovisioning apps/api/src/Application/DependencyInjection.cs apps/api/tests/IntegrationTests/TenantHardDeleteHandlerTests.cs
git commit -m "feat(deprovisioning): drop the tenant database, role, and credential on hard-delete"
```

---

### Task 11: Admin uç noktası — tenant silmeyi tetikle

**Files:**
- Modify: `apps/api/src/ControlPlane/TenantEndpoints.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantProvisioningEndpointTests.cs`

**Interfaces:**
- Consumes: `Tenant.BeginDeprovisioning()` (mevcut), `OutboxMessage.CreateDelayed` (Task 8), `TenantHardDeleteRequested` (Task 10).
- Produces: `DELETE /admin/api/v1/tenants/{id}` uç noktası.

- [x] **Step 1: Başarısız testi yaz**

`TenantProvisioningEndpointTests.cs` gerçek fixture'ı `ControlPlaneFixture` (`fixture.Client`, `fixture.CreateControlPlaneDbContext()`) kullanıyor. Aynı dosyaya ekle (dosyanın başına `using Domain.ControlPlane.Tenants;` ve `using Infrastructure.Messaging;` ekle):

```csharp
[Fact]
public async Task DeleteTenant_BeginsDeprovisioningAndSchedulesTheHardDeleteThirtyDaysOut()
{
    // Arrange
    await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
    Guid tenantId;

    await using (var context = fixture.CreateControlPlaneDbContext())
    {
        var tenant = Tenant.Create(TenantAlias.Create("acme"));
        tenant.CompleteProvisioning();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        tenantId = tenant.Id.Value;
    }

    // Act
    using var response = await fixture.Client.DeleteAsync(
        $"/admin/api/v1/tenants/{tenantId}", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    await using var assertions = fixture.CreateControlPlaneDbContext();
    var reloaded = await assertions.Tenants.SingleAsync(
        t => t.Id.Value == tenantId, TestContext.Current.CancellationToken);
    Assert.Equal(TenantStatus.Deprovisioning, reloaded.Status);
    var message = await assertions.OutboxMessages.SingleAsync(
        m => m.Type == TenantHardDeleteRequested.MessageType, TestContext.Current.CancellationToken);
    Assert.True(message.NextAttemptAt > DateTimeOffset.UtcNow.AddDays(29));
}

[Fact]
public async Task DeleteTenant_ForAProvisioningTenant_ReturnsConflict()
{
    // Arrange
    await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
    using var created = await fixture.Client.PostAsJsonAsync(
        "/admin/api/v1/tenants", new { alias = "acme" }, TestContext.Current.CancellationToken);
    var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    var tenantId = body.GetProperty("id").GetGuid();

    // Act
    using var response = await fixture.Client.DeleteAsync(
        $"/admin/api/v1/tenants/{tenantId}", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task DeleteTenant_ForAnUnknownIdentifier_ReturnsNotFound()
{
    // Arrange
    await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

    // Act
    using var response = await fixture.Client.DeleteAsync(
        $"/admin/api/v1/tenants/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

- [x] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisioningEndpointTests"`
Expected: FAIL (404 — uç nokta yok)

- [x] **Step 3: Uç noktayı ekle**

`TenantEndpoints.cs`'e ekle:

```csharp
group.MapDelete("/tenants/{id:guid}", DeleteAsync);
```

```csharp
private static readonly TimeSpan HardDeleteGracePeriod = TimeSpan.FromDays(30);

private static async Task<IResult> DeleteAsync(
    Guid id,
    ControlPlaneDbContext dbContext,
    TimeProvider timeProvider,
    CancellationToken cancellationToken)
{
    var tenantId = new TenantId(id);
    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
        candidate => candidate.Id == tenantId, cancellationToken);

    if (tenant is null)
    {
        return Results.NotFound();
    }

    if (tenant.Status is not (TenantStatus.Active or TenantStatus.Suspended))
    {
        return Results.Conflict(new { error = $"Tenant is {tenant.Status}, cannot be deleted." });
    }

    var now = timeProvider.GetUtcNow();
    tenant.BeginDeprovisioning();
    dbContext.OutboxMessages.Add(OutboxMessage.CreateDelayed(
        Guid.CreateVersion7(),
        TenantHardDeleteRequested.MessageType,
        JsonSerializer.Serialize(new TenantHardDeleteRequested(tenant.Id.Value)),
        now,
        now + HardDeleteGracePeriod));
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted(value: ToDetail(tenant));
}
```

- [x] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantProvisioningEndpointTests"`
Expected: PASS

- [x] **Step 5: Commit**

```bash
git add apps/api/src/ControlPlane/TenantEndpoints.cs apps/api/tests/IntegrationTests/TenantProvisioningEndpointTests.cs
git commit -m "feat(admin): add tenant delete endpoint with a 30-day grace period"
```

---

### Task 12: `TenantDbContextFactory` — tenant-özel credential overload'u

**Files:**
- Modify: `apps/api/src/Infrastructure/Persistence/Tenants/TenantDbContextFactory.cs`
- Test: `apps/api/tests/IntegrationTests/TenantDbContextScopeTests.cs`

**Interfaces:**
- Produces: `TenantDbContextFactory.Create(string databaseName, string username, string password) -> TenantDbContext`. Mevcut `Create(string databaseName)` değişmez. Task 13 bunu kullanır.

- [ ] **Step 1: Mevcut `TenantDbContextScopeTests.cs`'i oku, sonra başarısız testi ekle**

```csharp
[Fact]
public async Task Create_WithCredentials_ConnectsAsTheGivenRole()
{
    // Arrange
    await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var provisioner = new TenantProvisioner(postgres.GetConnectionString(), new AuditInterceptor(TimeProvider.System));
    var databaseName = TenantDatabaseName.Create("tenant_acme");
    var roleName = TenantRoleName.Create("access_acme");
    await provisioner.CreateDatabaseAsync(databaseName, TestContext.Current.CancellationToken);
    await provisioner.MigrateSchemaAsync(databaseName, TestContext.Current.CancellationToken);
    var password = await provisioner.GrantTenantAccessAsync(
        databaseName, roleName, TestContext.Current.CancellationToken);
    var factory = new TenantDbContextFactory(postgres.GetConnectionString(), new AuditInterceptor(TimeProvider.System));

    // Act
    await using var context = factory.Create(databaseName.Value, roleName.Value, password);
    var count = await context.Users.CountAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(0, count);
}
```

- [ ] **Step 2: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantDbContextScopeTests"`
Expected: FAIL (derleme hatası — overload yok)

- [ ] **Step 3: Overload'u ekle**

`TenantDbContextFactory.cs`'e ekle:
```csharp
public TenantDbContext Create(string databaseName, string username, string password)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
    ArgumentException.ThrowIfNullOrWhiteSpace(username);
    ArgumentException.ThrowIfNullOrWhiteSpace(password);

    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Database = databaseName,
        Username = username,
        Password = password
    };
    var options = new DbContextOptionsBuilder<TenantDbContext>()
        .UseNpgsql(builder.ConnectionString)
        .AddInterceptors(auditInterceptor)
        .Options;

    return new TenantDbContext(options);
}
```

- [ ] **Step 4: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantDbContextScopeTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Infrastructure/Persistence/Tenants/TenantDbContextFactory.cs apps/api/tests/IntegrationTests/TenantDbContextScopeTests.cs
git commit -m "feat(persistence): add a credential-aware TenantDbContextFactory overload"
```

---

### Task 13: İstek yolunu bağla — `TenantContext`, middleware, `Program.cs`

**Files:**
- Modify: `apps/api/src/Application/Abstractions/TenantContext.cs`
- Modify: `apps/api/src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Modify: `apps/api/tests/IntegrationTests/TenantAccessEndpointTests.cs`

**Interfaces:**
- Consumes: `TenantCredentialResolver` (Task 9), `TenantDbContextFactory.Create(databaseName, username, password)` (Task 12), `TenantProvisioner.GrantTenantAccessAsync` (Task 5), `TenantCredential.ProtectionPurpose` (Task 3).
- Produces: `TenantContext.Username`, `TenantContext.Password` — bunları okuyan tek yer `Program.cs`'deki scoped `TenantDbContext` factory'si.

**Önemli — kapsamı genişleten bir bulgu:** `TenantSurfaceFixture` (7 test dosyası tarafından paylaşılıyor: `AuditStampTests`, `InvitationAcceptanceTests`, `InvitationEndpointTests`, `MemberEndpointTests`, `ProblemDetailsTests`, `RoleEndpointTests`, `UnitOfWorkBehaviorTests`) bugün tenant DB'sini ve kullanıcıyı doğrudan EF ile yazıyor, `TenantProvisioner.GrantTenantAccessAsync`'i hiç çağırmıyor — yani hiçbir `tenant_credentials` satırı yok. Middleware artık credential çözümlemesini zorunlu kılınca, bu fixture düzeltilmeden yukarıdaki 7 dosyanın TÜMÜ 503 almaya başlar. Bu yüzden Step 1 önce fixture'ı düzeltiyor.

- [ ] **Step 1: `TenantSurfaceFixture.StartAsync`'i credential seed edecek şekilde düzelt**

`TenantSurfaceFixture.cs`'de `StartAsync(string, string?, string?)` metodunu değiştir — `factory`'yi tenant DB kurulumundan hemen sonra, dönüş ifadesinden önce oluştur, sonra factory'nin KENDİ DI konteynerinden `TenantProvisioner` ve `IDataProtectionProvider`'ı çözüp credential'ı gerçek akışla üret (fixture'ın kendi ayrı bir `DataProtectionProvider` yaratmaması önemli — key ring uyuşmazsa `Unprotect` patlar):

```csharp
public static async Task<TenantSurfaceFixture> StartAsync(
    string externalUserId,
    string? email,
    string? roleCode)
{
    var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    await postgres.StartAsync(TestContext.Current.CancellationToken);
    var connectionString = postgres.GetConnectionString();
    await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
    var controlPlane = WithDatabase(connectionString, "control_plane");
    var tenant = Tenant.Create(TenantAlias.Create(Alias));
    tenant.CompleteProvisioning();

    await using (var context = CreateControlPlaneDbContext(controlPlane))
    {
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        context.Tenants.Add(tenant);
        context.Memberships.Add(Membership.Create(
            tenant.Id,
            ExternalUserId.Create(externalUserId)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    await ExecuteAsync(connectionString, $"CREATE DATABASE {tenant.DatabaseName.Value}");

    await using (var tenantDbContext =
                 new TenantDbContextFactory(connectionString, AuditInterceptor).Create(tenant.DatabaseName.Value))
    {
        await tenantDbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        if (roleCode is not null)
        {
            var role = await tenantDbContext.Roles.SingleAsync(
                candidate => candidate.Code == roleCode,
                TestContext.Current.CancellationToken);
            var user = User.Create(
                ExternalUserId.Create(externalUserId),
                EmailAddress.Create(email!),
                UserStatus.Active,
                role);
            tenantDbContext.Users.Add(user);
            await tenantDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    var factory = BuildFactory(controlPlane, connectionString, externalUserId, email);

    using (var scope = factory.Services.CreateScope())
    {
        var provisioner = scope.ServiceProvider.GetRequiredService<Infrastructure.Provisioning.TenantProvisioner>();
        var dataProtectionProvider = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var roleName = TenantRoleName.ForTenant(tenant.Id);
        var password = await provisioner.GrantTenantAccessAsync(
            tenant.DatabaseName, roleName, TestContext.Current.CancellationToken);
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);

        await using var controlPlaneContext = CreateControlPlaneDbContext(controlPlane);
        controlPlaneContext.TenantCredentials.Add(
            TenantCredential.Create(tenant.Id, roleName, protector.Protect(password)));
        await controlPlaneContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    return new TenantSurfaceFixture(
        postgres,
        factory,
        tenant,
        controlPlane,
        ownsContainer: true);
}
```

`using Domain.ControlPlane.Tenants;` zaten dosyada var (`TenantCredential`, `TenantRoleName` aynı namespace). `using Microsoft.Extensions.DependencyInjection;` zaten var (`CreateScope`/`GetRequiredService` için).

- [ ] **Step 2: Başarısız testi yaz — credential yoksa 503**

`TenantAccessEndpointTests.cs`'e ekle (mevcut testlerin yanına):
```csharp
[Fact]
public async Task Request_WhenTheTenantHasNoStoredCredential_ReturnsServiceUnavailable()
{
    // Arrange
    await using var fixture = await TenantSurfaceFixture.StartAsync();
    await using (var context = fixture.CreateControlPlane())
    {
        var credential = await context.TenantCredentials.SingleAsync(
            c => c.TenantId == fixture.Tenant.Id, TestContext.Current.CancellationToken);
        context.TenantCredentials.Remove(credential);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // Act
    using var response = await fixture.Client.GetAsync(
        "/acme/api/v1/me/memberships", TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}
```

Bu tek test, hem yeni 503 dalını hem de (dosyadaki mevcut, credential-seed'li fixture'a karşı çalışan diğer bütün testlerin geçmeye devam etmesiyle) mutlu yolu kanıtlıyor — ayrıca "current_user doğru role mu" diye `pg_stat_activity` gibi kırılgan bir kontrole gerek yok: Task 12'nin testi zaten `TenantDbContextFactory.Create(databaseName, username, password)`'ın doğru role ile gerçekten bağlanıp sorgu çalıştırdığını kanıtlamıştı.

- [ ] **Step 3: Testin başarısız olduğunu gör**

Run: `dotnet test --solution Api.slnx --filter "FullyQualifiedName~TenantAccessEndpointTests"`
Expected: FAIL (derleme hatası — `TenantContext.Set` henüz 5 parametre almıyor, `TenantCredentialResolver` middleware'de yok)

- [ ] **Step 4: `TenantContext`'i genişlet**

```csharp
using Domain.ControlPlane.Tenants;

namespace Application.Abstractions;

public sealed class TenantContext
{
    private TenantId? tenantId;
    private string? alias;
    private string? databaseName;
    private string? username;
    private string? password;

    public bool IsResolved => tenantId is not null;

    public TenantId TenantId => tenantId ?? throw NotResolved();

    public string Alias => alias ?? throw NotResolved();

    public string DatabaseName => databaseName ?? throw NotResolved();

    public string Username => username ?? throw NotResolved();

    public string Password => password ?? throw NotResolved();

    public void Set(
        TenantId resolvedTenantId,
        string resolvedAlias,
        string resolvedDatabaseName,
        string resolvedUsername,
        string resolvedPassword)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
        databaseName = resolvedDatabaseName;
        username = resolvedUsername;
        password = resolvedPassword;
    }

    private static InvalidOperationException NotResolved() =>
        new("Tenant context has not been resolved.");
}
```

- [ ] **Step 5: `TenantAccessMiddleware`'i güncelle**

`TenantAccessMiddleware.cs`'de, `tenantContext.Set(...)` çağrısından önce credential çöz:

```csharp
var credentialResolver = services.GetRequiredService<TenantCredentialResolver>();
var credential = await credentialResolver.ResolveAsync(tenant.Id, context.RequestAborted);

if (credential is null)
{
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    return;
}

tenantContext.Set(TenantId.From(tenant.Id), tenant.Alias, tenant.DatabaseName, credential.RoleName, credential.Password);
```

(`var hasMembership = ...` satırından SONRA, mevcut `tenantContext.Set(...)` satırının yerine geçer — sıralamayı koru: membership kontrolü hâlâ credential çözmeden önce, gereksiz bir DB round-trip'i erken 403 ile engellemek için.)

- [ ] **Step 6: `Program.cs`'i güncelle**

```csharp
builder.Services.AddScoped<TenantCredentialResolver>();
builder.Services.AddScoped(provider =>
{
    var tenantContext = provider.GetRequiredService<TenantContext>();
    var factory = provider.GetRequiredService<TenantDbContextFactory>();

    return factory.Create(tenantContext.DatabaseName, tenantContext.Username, tenantContext.Password);
});
```

(`using Infrastructure.Tenants;` zaten `Program.cs`'de var — `TenantResolver` için.)

- [ ] **Step 7: Testin geçtiğini gör**

Run: `dotnet test --solution Api.slnx`
Expected: PASS (tüm testler — özellikle `TenantSurfaceFixture`'ı kullanan 7 dosyanın hepsi, Step 1'deki fixture düzeltmesi sayesinde hâlâ yeşil)

- [ ] **Step 8: Commit**

```bash
git add apps/api/src/Application/Abstractions/TenantContext.cs apps/api/src/Api/Tenants/TenantAccessMiddleware.cs apps/api/src/Api/Program.cs apps/api/tests/IntegrationTests/TenantAccessEndpointTests.cs apps/api/tests/IntegrationTests/TenantSurfaceFixture.cs
git commit -m "feat(api): connect to each tenant database with its dedicated role"
```

---

## Definition of Done

- [ ] `git log --oneline` on üç görev commit'ini gösteriyor.
- [ ] `grep -rn --include='*.cs' --include='*.sql' "st_tenant\|st_migrator\|st_provisioner\|st_control\b" apps/api` sonuç döndürmüyor.
- [ ] `dotnet test --solution Api.slnx` yeşil.
- [ ] `TenantIsolationTests` projesindeki testler, tenant-özel role'ün control plane'e hiç erişemediğini ve başka bir tenant'ın DB'sine bağlanamadığını kanıtlıyor (mevcut `CredentialBoundaryTests` zaten `resolver`'ı test ediyor; tenant-özel role için benzer bir test eklemek isteğe bağlı bir sonraki adım — bu plan kapsamında zorunlu değil, spec'in "yapılmadıklarımız" tablosuna düşülebilir).
- [ ] Bootstrap script'leri (`bootstrap-roles.sql`, `grant-control-plane.sql`) yeni isimlerle tutarlı.
