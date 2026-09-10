# Tenant Provisioning Workflow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bir platform admininin tenant oluşturma isteğinden, o tenant'a giriş yapılabilir hale gelmesine kadar geçen akışı outbox üzerinden, adım adım takip edilebilir ve yeniden denenebilir şekilde kurmak.

**Architecture:** Tenant kaydı ve outbox mesajı aynı transaction'da yazılır. Arka planda çalışan bir worker mesajı `SELECT ... FOR UPDATE SKIP LOCKED` ile alır ve tek bir handler beş idempotent adımı baştan yürütür. İlerleme tenant kaydındaki iki kolonda raporlanır; doğruluk adımların idempotent olmasından gelir, kontrol akışından değil.

**Tech Stack:** .NET 10, PostgreSQL 18, EF Core 10, Dapper, Npgsql, xUnit v3 + Microsoft.Testing.Platform, Testcontainers.

**Spec:** [2026-09-09-tenant-provisioning-workflow-design.md](../specs/2026-09-09-tenant-provisioning-workflow-design.md)
**Önkoşul:** Spec 1 uygulanmış durumda (commit `8130eed`).

## Global Constraints

- Hedef framework `net10.0`; tablo ve kolon adları `snake_case`; GUID birincil anahtarlar `ValueGeneratedNever()`.
- Value object'ler EF'e `HasConversion` ile bağlanır.
- Testler xUnit v3 kullanır ve `TestContext.Current.CancellationToken` alır; açık `// Arrange`, `// Act`, `// Assert` etiketleri zorunludur.
- Kalıcılık testleri gerçek PostgreSQL'e karşı Testcontainers ile çalışır.
- Domain katmanı ASP.NET Core, EF Core ve dış servislerden bağımsız kalır.
- Zaman damgaları `timestamptz` ve UTC; backend `TimeProvider` üzerinden üretir, istemciden gelmez.
- Ham SQL'e giden veritabanı adları yalnızca `TenantDatabaseName` value object'i üzerinden geçer; doğrulanmamış string kabul eden bir DDL metodu yazılmaz.
- Migration'lar deploy adımıdır; uygulama başlangıcında migration çalıştırılmaz.
- Kod yorumları yalnızca aşikâr olmayan kısıtı açıklar.
- Test komutu: `dotnet test --solution apps/api/Api.slnx` (tek proje için `dotnet test --project <csproj>`).

## Dosya Yapısı

**Yeni dosyalar**
- `src/Domain/Tenants/TenantProvisioningStep.cs` — provisioning adımı enum'u
- `src/Infrastructure/Messaging/OutboxMessage.cs` — outbox kaydı ve deneme muhasebesi
- `src/Infrastructure/Messaging/OutboxDrainer.cs` — bir turluk kuyruk boşaltma; testlerin doğrudan çağırdığı yer
- `src/Infrastructure/Messaging/OutboxProcessor.cs` — `BackgroundService`, yalnızca zamanlayıcı
- `src/Infrastructure/Messaging/TenantProvisioningRequested.cs` — mesaj gövdesi
- `src/Infrastructure/Persistence/ControlPlane/Configurations/OutboxMessageConfiguration.cs`
- `src/Infrastructure/Provisioning/TenantProvisioner.cs` — `CREATE DATABASE`, şema, grant'lar
- `src/Infrastructure/Provisioning/TenantProvisioningHandler.cs` — beş adım
- `src/ControlPlane/TenantEndpoints.cs` — tenant oluşturma, detay, yeniden deneme
- `tests/IntegrationTests/ProvisioningFixture.cs` — ortak Testcontainers kurulumu

**Değiştirilen**
- `src/Domain/Tenants/Tenant.cs` — provisioning ilerlemesi
- `src/Domain/Access/Users/TenantUser.cs`, `TenantUserRole.cs` — factory metotları
- `src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs` — `OutboxMessages`
- `src/Infrastructure/Persistence/ControlPlane/Configurations/TenantConfiguration.cs` — iki yeni kolon
- `src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs` — provisioner bağlantısı ve servisler
- `src/ControlPlane/ControlPlaneEndpoints.cs` — yeni endpoint grubunu bağlar
- `src/Api/Program.cs` — worker kaydı
- `scripts/bootstrap-roles.sql` — `st_migrator`'a `st_provisioner` üyeliği
- `apps/api/src/Api/appsettings*.json` — `Provisioner` bağlantısı
- `apps/api/AGENTS.md`

---

### Görev 1: Outbox tablosu ve deneme muhasebesi

**Files:**
- Create: `apps/api/src/Infrastructure/Messaging/OutboxMessage.cs`
- Create: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/OutboxMessageConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs`
- Test: `apps/api/tests/IntegrationTests/OutboxMessageTests.cs`

**Interfaces:**
- Produces: `OutboxMessage.Create(Guid id, string type, string payload, DateTimeOffset createdAt)`;
  `OutboxMessage.MarkProcessed(DateTimeOffset)`; `OutboxMessage.RecordFailure(string error, DateTimeOffset now)`;
  `OutboxMessage.MaximumAttempts`; `control.outbox_messages` tablosu.

- [ ] **Step 1: Başarısız testi yaz**

`apps/api/tests/IntegrationTests/OutboxMessageTests.cs`:

```csharp
using Infrastructure.Messaging;
using Xunit;

namespace IntegrationTests;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsUnprocessedAndImmediatelyDue()
    {
        // Arrange & Act
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Assert
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(Now, message.NextAttemptAt);
    }

    [Fact]
    public void RecordFailure_CountsTheAttemptAndBacksOff()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.RecordFailure("boom", Now);

        // Assert
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("boom", message.LastError);
        Assert.Equal(Now.AddSeconds(10), message.NextAttemptAt);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public void RecordFailure_BacksOffFurtherOnEachAttempt()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.RecordFailure("boom", Now);
        message.RecordFailure("boom", Now);
        message.RecordFailure("boom", Now);

        // Assert
        Assert.Equal(3, message.AttemptCount);
        Assert.Equal(Now.AddSeconds(40), message.NextAttemptAt);
    }

    [Fact]
    public void MarkProcessed_StampsTheCompletionTime()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.MarkProcessed(Now.AddSeconds(5));

        // Assert
        Assert.Equal(Now.AddSeconds(5), message.ProcessedAt);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `OutboxMessage` bulunamıyor.

- [ ] **Step 3: `OutboxMessage`'ı yaz**

`apps/api/src/Infrastructure/Messaging/OutboxMessage.cs`:

```csharp
namespace Infrastructure.Messaging;

public sealed class OutboxMessage
{
    public const int MaximumAttempts = 5;

    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(10);

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage Create(Guid id, string type, string payload, DateTimeOffset createdAt)
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
            NextAttemptAt = createdAt
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt) => ProcessedAt = processedAt;

    public void RecordFailure(string error, DateTimeOffset now)
    {
        AttemptCount++;
        LastError = error;
        NextAttemptAt = now + BaseBackoff * Math.Pow(2, AttemptCount - 1);
    }
}
```

- [ ] **Step 4: EF yapılandırmasını yaz**

`apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/OutboxMessageConfiguration.cs`:

```csharp
using Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error");
        builder.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
    }
}
```

- [ ] **Step 5: `DbSet`'i ekle**

`apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs` içine, `PlatformAdmins` satırından sonra:

```csharp
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
```

Dosyanın başına `using Infrastructure.Messaging;` ekle.

- [ ] **Step 6: Migration üret**

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add AddOutboxMessages \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
```

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `OutboxMessageTests` içindeki dört test PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): record outbox messages in the control plane"
```

---

### Görev 2: Tenant aggregate'inde provisioning ilerlemesi

**Files:**
- Create: `apps/api/src/Domain/Tenants/TenantProvisioningStep.cs`
- Modify: `apps/api/src/Domain/Tenants/Tenant.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/TenantConfiguration.cs`
- Test: `apps/api/tests/UnitTests/Tenants/TenantTests.cs`

**Interfaces:**
- Produces: `TenantProvisioningStep` enum'u (`CreatingDatabase`, `MigratingSchema`, `GrantingAccess`, `SeedingOwner`);
  `Tenant.ProvisioningStep`, `Tenant.ProvisioningError`;
  `Tenant.RecordProvisioningProgress(TenantProvisioningStep, DateTimeOffset)`;
  `Tenant.RecordProvisioningFailure(TenantProvisioningStep, string, DateTimeOffset)`;
  `Tenant.CompleteProvisioning(DateTimeOffset)`.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/UnitTests/Tenants/TenantTests.cs` sınıfına ekle:

```csharp
    [Fact]
    public void Create_StartsAtTheFirstProvisioningStep()
    {
        // Arrange & Act
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);

        // Assert
        Assert.Equal(TenantProvisioningStep.CreatingDatabase, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
    }

    [Fact]
    public void RecordProvisioningProgress_MovesTheStepAndClearsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
        tenant.RecordProvisioningFailure(TenantProvisioningStep.CreatingDatabase, "boom", CreatedAt);

        // Act
        tenant.RecordProvisioningProgress(TenantProvisioningStep.MigratingSchema, CreatedAt.AddSeconds(1));

        // Assert
        Assert.Equal(TenantProvisioningStep.MigratingSchema, tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
        Assert.Equal(CreatedAt.AddSeconds(1), tenant.UpdatedAt);
    }

    [Fact]
    public void RecordProvisioningFailure_KeepsTheTenantProvisioningAndRecordsTheError()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);

        // Act
        tenant.RecordProvisioningFailure(TenantProvisioningStep.GrantingAccess, "denied", CreatedAt.AddSeconds(2));

        // Assert
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(TenantProvisioningStep.GrantingAccess, tenant.ProvisioningStep);
        Assert.Equal("denied", tenant.ProvisioningError);
    }

    [Fact]
    public void CompleteProvisioning_ActivatesTheTenantAndClearsProgress()
    {
        // Arrange
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create("acme"), CreatedAt);
        tenant.RecordProvisioningFailure(TenantProvisioningStep.SeedingOwner, "boom", CreatedAt);

        // Act
        tenant.CompleteProvisioning(CreatedAt.AddSeconds(3));

        // Assert
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(tenant.ProvisioningStep);
        Assert.Null(tenant.ProvisioningError);
        Assert.Equal(CreatedAt.AddSeconds(3), tenant.UpdatedAt);
    }
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Derleme hatası — `TenantProvisioningStep` bulunamıyor.

- [ ] **Step 3: Enum'u yaz**

`apps/api/src/Domain/Tenants/TenantProvisioningStep.cs`:

```csharp
namespace Domain.Tenants;

public enum TenantProvisioningStep
{
    CreatingDatabase,
    MigratingSchema,
    GrantingAccess,
    SeedingOwner
}
```

- [ ] **Step 4: `Tenant`'ı genişlet**

`apps/api/src/Domain/Tenants/Tenant.cs` içine, `UpdatedAt` özelliğinden sonra ekle:

```csharp
    public TenantProvisioningStep? ProvisioningStep { get; private set; }

    public string? ProvisioningError { get; private set; }
```

`Create` metodundaki nesne başlatıcısına ekle:

```csharp
            ProvisioningStep = TenantProvisioningStep.CreatingDatabase,
```

Sınıfın sonuna üç metot ekle:

```csharp
    public void RecordProvisioningProgress(TenantProvisioningStep step, DateTimeOffset updatedAt)
    {
        ProvisioningStep = step;
        ProvisioningError = null;
        UpdatedAt = updatedAt;
    }

    public void RecordProvisioningFailure(TenantProvisioningStep step, string error, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        ProvisioningStep = step;
        ProvisioningError = error;
        UpdatedAt = updatedAt;
    }

    public void CompleteProvisioning(DateTimeOffset updatedAt)
    {
        Status = TenantStatus.Active;
        ProvisioningStep = null;
        ProvisioningError = null;
        UpdatedAt = updatedAt;
    }
```

- [ ] **Step 5: EF yapılandırmasına kolonları ekle**

`TenantConfiguration.Configure` içine, `UpdatedAt` satırından sonra:

```csharp
        builder.Property(x => x.ProvisioningStep)
            .HasColumnName("provisioning_step")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.ProvisioningError).HasColumnName("provisioning_error");
```

- [ ] **Step 6: Migration üret**

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add AddTenantProvisioningProgress \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
```

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): track provisioning progress on the tenant record"
```

---

### Görev 3: Tenant kullanıcısı ve rol ataması için factory metotları

**Files:**
- Modify: `apps/api/src/Domain/Access/Users/TenantUser.cs`
- Modify: `apps/api/src/Domain/Access/Users/TenantUserRole.cs`
- Test: `apps/api/tests/UnitTests/Access/TenantUserTests.cs`

**Interfaces:**
- Produces: `TenantUser.Create(Guid id, ExternalUserId externalUserId, TenantUserStatus status)`;
  `TenantUserRole.Create(Guid userId, Guid roleId)`.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/UnitTests/Access/TenantUserTests.cs`:

```csharp
using Domain.Access;
using Domain.Access.Users;
using Xunit;

namespace UnitTests.Access;

public sealed class TenantUserTests
{
    [Fact]
    public void Create_ForAnEmptyIdentifier_Throws()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");

        // Act
        var act = () => TenantUser.Create(Guid.Empty, externalUserId, TenantUserStatus.Active);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_KeepsTheIdentityAndStatus()
    {
        // Arrange
        var externalUserId = ExternalUserId.Create("user_2abc123");
        var id = Guid.NewGuid();

        // Act
        var user = TenantUser.Create(id, externalUserId, TenantUserStatus.Active);

        // Assert
        Assert.Equal(id, user.Id);
        Assert.Equal(externalUserId, user.ExternalUserId);
        Assert.Equal(TenantUserStatus.Active, user.Status);
    }

    [Fact]
    public void CreateRole_ForAnEmptyRole_Throws()
    {
        // Arrange & Act
        var act = () => TenantUserRole.Create(Guid.NewGuid(), Guid.Empty);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void CreateRole_KeepsBothIdentifiers()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        // Act
        var assignment = TenantUserRole.Create(userId, roleId);

        // Assert
        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(roleId, assignment.RoleId);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: Derleme hatası — `TenantUser.Create` yok.

- [ ] **Step 3: Factory metotlarını yaz**

`apps/api/src/Domain/Access/Users/TenantUser.cs` dosyasının tamamını şununla değiştir:

```csharp
namespace Domain.Access.Users;

public enum TenantUserStatus { Active, Disabled }

public sealed class TenantUser
{
    public Guid Id { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public TenantUserStatus Status { get; private set; }

    private TenantUser()
    {
    }

    public static TenantUser Create(Guid id, ExternalUserId externalUserId, TenantUserStatus status)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new TenantUser
        {
            Id = id,
            ExternalUserId = externalUserId,
            Status = status
        };
    }
}
```

`apps/api/src/Domain/Access/Users/TenantUserRole.cs` dosyasının tamamını şununla değiştir:

```csharp
namespace Domain.Access.Users;

public sealed class TenantUserRole
{
    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    private TenantUserRole()
    {
    }

    public static TenantUserRole Create(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(userId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(roleId));
        }

        return new TenantUserRole
        {
            UserId = userId,
            RoleId = roleId
        };
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 5: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): construct tenant users through the domain"
```

---

### Görev 4: `TenantProvisioner` — veritabanı, şema ve yetkiler

**Files:**
- Create: `apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs`
- Modify: `apps/api/scripts/bootstrap-roles.sql`
- Modify: `apps/api/src/Api/appsettings.json`, `appsettings.Development.json`
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Test: `apps/api/tests/IntegrationTests/TenantProvisionerTests.cs`

**Interfaces:**
- Consumes: `TenantDatabaseName`, `TenantSchemaMigrator`, `TenantDbContextFactory`.
- Produces: `TenantProvisioner.CreateDatabaseAsync(TenantDatabaseName, CancellationToken)`;
  `TenantProvisioner.MigrateSchemaAsync(TenantDatabaseName, CancellationToken)`;
  `TenantProvisioner.GrantTenantAccessAsync(TenantDatabaseName, CancellationToken)`;
  `ConnectionStrings:Provisioner` yapılandırma anahtarı.

- [ ] **Step 1: `st_migrator`'ı `st_provisioner` üyesi yap**

Provisioning, tenant veritabanını `st_provisioner` ile oluşturur ve tablolarının sahibi o olur.
Deploy zamanındaki `migrate tenants` ise `st_migrator` ile çalışır ve sahip olmadığı tabloları
değiştiremez. Üyelik bu boşluğu kapatır; `st_provisioner` control plane'e hâlâ erişemez.

`apps/api/scripts/bootstrap-roles.sql` dosyasının sonuna ekle:

```sql

-- The migrator alters tenant tables that the provisioner owns, so it must be able to act as their owner.
GRANT st_provisioner TO st_migrator;
```

- [ ] **Step 2: Provisioner bağlantısını yapılandırmaya ekle**

`apps/api/src/Api/appsettings.json` içindeki `ConnectionStrings` nesnesine `"Provisioner": ""` ekle.

`apps/api/src/Api/appsettings.Development.json` içindeki `ConnectionStrings` nesnesine ekle:

```json
    "Provisioner": "Host=localhost;Port=5432;Database=postgres;Username=st_provisioner;Password=dev_provisioner"
```

`AddTenantPersistence` bu anahtarı zorunlu kılacağı için, uygulamayı ayağa kaldıran iki test
yardımcısı da bunu sağlamalıdır. `apps/api/tests/IntegrationTests/ControlPlaneFixture.cs`
içindeki `WithWebHostBuilder` bloğuna ve
`apps/api/tests/IntegrationTests/TenantAccessEndpointTests.cs` içindeki `CreateFactory`
metoduna ekle:

```csharp
            builder.UseSetting("ConnectionStrings:Provisioner", connectionString);
```

`TenantAccessEndpointTests` içinde bu satır `connectionString` yerel değişkenini kullanır;
`ControlPlaneFixture` içinde de aynı adlı değişken mevcuttur.

- [ ] **Step 3: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/TenantProvisionerTests.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Provisioning;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisionerTests
{
    private static readonly TenantDatabaseName DatabaseName = TenantDatabaseName.Create("tenant_acme");

    [Fact]
    public async Task CreateDatabaseAsync_CreatesTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task CreateDatabaseAsync_RunTwice_Succeeds()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task MigrateSchemaAsync_CreatesTheTenantTables()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await UsersTableExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task GrantTenantAccessAsync_RunTwice_Succeeds()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "CREATE ROLE st_tenant LOGIN PASSWORD 'test'");
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.GrantTenantAccessAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.GrantTenantAccessAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = DatabaseName.Value,
            Username = "st_tenant",
            Password = "test"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(tenantConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
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

    private static async Task<bool> UsersTableExistsAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is "users";
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

- [ ] **Step 4: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `TenantProvisioner` bulunamıyor.

- [ ] **Step 5: `TenantProvisioner`'ı yaz**

Metotlar yalnızca `TenantDatabaseName` kabul eder; ham SQL'e giden tek yol doğrulanmış bir
değerdir.

`apps/api/src/Infrastructure/Provisioning/TenantProvisioner.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Persistence.Tenants;
using Npgsql;

namespace Infrastructure.Provisioning;

public sealed class TenantProvisioner(string connectionString)
{
    private const string TenantRole = "st_tenant";

    public async Task CreateDatabaseAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)",
            connection);
        existsCommand.Parameters.AddWithValue("name", databaseName.Value);

        if ((bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!)
        {
            return;
        }

        await using var createCommand = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName.Value}\"",
            connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MigrateSchemaAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        return migrator.MigrateAsync(databaseName.Value, cancellationToken);
    }

    public async Task GrantTenantAccessAsync(TenantDatabaseName databaseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString());
        await maintenance.OpenAsync(cancellationToken);
        await using var connectCommand = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE \"{databaseName.Value}\" TO {TenantRole}",
            maintenance);
        await connectCommand.ExecuteNonQueryAsync(cancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName.Value };
        await using var tenant = new NpgsqlConnection(builder.ConnectionString);
        await tenant.OpenAsync(cancellationToken);
        await using var grantCommand = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA public TO {TenantRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {TenantRole};
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {TenantRole};
            """,
            tenant);
        await grantCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private string MaintenanceConnectionString() =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
}
```

- [ ] **Step 6: Provisioner'ı DI'a kaydet**

`PostgresServiceCollectionExtensions.AddTenantPersistence` içine, `return services;` satırından önce:

```csharp
        services.AddSingleton(new TenantProvisioner(
            RequiredConnectionString(configuration, "Provisioner")));
```

Dosyanın başına `using Infrastructure.Provisioning;` ekle.

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `TenantProvisionerTests` içindeki dört test PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): provision tenant databases under a dedicated role"
```

---

### Görev 5: Provisioning handler — beş idempotent adım

**Files:**
- Create: `apps/api/src/Infrastructure/Messaging/TenantProvisioningRequested.cs`
- Create: `apps/api/src/Infrastructure/Provisioning/TenantProvisioningHandler.cs`
- Create: `apps/api/tests/IntegrationTests/ProvisioningFixture.cs`
- Test: `apps/api/tests/IntegrationTests/TenantProvisioningHandlerTests.cs`

**Interfaces:**
- Consumes: Görev 2'nin `Tenant` metotları, Görev 3'ün factory'leri, Görev 4'ün `TenantProvisioner`'ı.
- Produces: `TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId)`;
  `TenantProvisioningHandler.HandleAsync(TenantProvisioningRequested, CancellationToken)`;
  `TenantProvisioningHandler.MessageType` sabiti.

- [ ] **Step 1: Ortak test fixture'ını yaz**

`apps/api/tests/IntegrationTests/ProvisioningFixture.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class ProvisioningFixture : IAsyncDisposable
{
    public const string OwnerExternalUserId = "user_2abc123";

    private readonly PostgreSqlContainer postgres;

    private ProvisioningFixture(PostgreSqlContainer postgres, string controlPlaneConnectionString)
    {
        this.postgres = postgres;
        ControlPlaneConnectionString = controlPlaneConnectionString;
        Provisioner = new TenantProvisioner(postgres.GetConnectionString());
    }

    public string ControlPlaneConnectionString { get; }

    public TenantProvisioner Provisioner { get; }

    public string ServerConnectionString => postgres.GetConnectionString();

    public static async Task<ProvisioningFixture> StartAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE ROLE st_tenant LOGIN PASSWORD 'test'");
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");

        await using (var context = CreateControlPlaneDbContext(controlPlane))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new ProvisioningFixture(postgres, controlPlane);
    }

    public ControlPlaneDbContext CreateControlPlane() => CreateControlPlaneDbContext(ControlPlaneConnectionString);

    public async Task<Tenant> AddProvisioningTenantAsync(string alias)
    {
        var tenant = Tenant.Create(Guid.NewGuid(), TenantAlias.Create(alias), DateTimeOffset.UtcNow);
        await using var context = CreateControlPlane();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return tenant;
    }

    public async Task<Tenant> ReloadAsync(Guid tenantId)
    {
        await using var context = CreateControlPlane();

        return await context.Tenants.SingleAsync(
            tenant => tenant.Id == tenantId,
            TestContext.Current.CancellationToken);
    }

    public async Task<long> CountAsync(string databaseName, string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    private static ControlPlaneDbContext CreateControlPlaneDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        return new ControlPlaneDbContext(options);
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

- [ ] **Step 2: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/TenantProvisioningHandlerTests.cs`:

```csharp
using Domain.Access;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisioningHandlerTests
{
    [Fact]
    public async Task HandleAsync_ProvisionsTheTenantAndActivatesIt()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningStep);
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users"));
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM user_roles"));
    }

    [Fact]
    public async Task HandleAsync_CreatesTheOwnerMembership()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await using var verification = fixture.CreateControlPlane();
        var userId = ExternalUserId.Create(ProvisioningFixture.OwnerExternalUserId);
        Assert.True(await verification.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == userId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_RunTwice_LeavesTheSameResult()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);
        var message = new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId);
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Act
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Equal(1, await fixture.CountAsync(tenant.DatabaseName.Value, "SELECT count(*) FROM users"));
        await using var verification = fixture.CreateControlPlane();
        Assert.Equal(1, await verification.Memberships.CountAsync(
            membership => membership.TenantId == tenant.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownTenant_Throws()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(Guid.NewGuid(), ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task HandleAsync_WhenAStepFails_RecordsTheStepAndRethrows()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(
            context,
            new TenantProvisioner("Host=127.0.0.1;Port=1;Username=nobody;Password=nobody"),
            TimeProvider.System);

        // Act
        var act = async () => await handler.HandleAsync(
            new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(act);
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Provisioning, reloaded.Status);
        Assert.Equal(TenantProvisioningStep.CreatingDatabase, reloaded.ProvisioningStep);
        Assert.NotNull(reloaded.ProvisioningError);
    }

    [Fact]
    public async Task HandleAsync_AfterAFailure_CompletesOnRetry()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await using var failingContext = fixture.CreateControlPlane();
        var failingHandler = new TenantProvisioningHandler(
            failingContext,
            new TenantProvisioner("Host=127.0.0.1;Port=1;Username=nobody;Password=nobody"),
            TimeProvider.System);
        var message = new TenantProvisioningRequested(tenant.Id, ProvisioningFixture.OwnerExternalUserId);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await failingHandler.HandleAsync(message, TestContext.Current.CancellationToken));

        // Act
        await using var context = fixture.CreateControlPlane();
        var handler = new TenantProvisioningHandler(context, fixture.Provisioner, TimeProvider.System);
        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        // Assert
        var reloaded = await fixture.ReloadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.ProvisioningError);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `TenantProvisioningHandler` bulunamıyor.

- [ ] **Step 4: Mesaj tipini yaz**

`apps/api/src/Infrastructure/Messaging/TenantProvisioningRequested.cs`:

```csharp
namespace Infrastructure.Messaging;

public sealed record TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId);
```

- [ ] **Step 5: Handler'ı yaz**

Adımlar her çalıştırmada baştan yürütülür; `provisioning_step` kolonu kontrol akışını değil
ilerlemeyi raporlar.

`apps/api/src/Infrastructure/Provisioning/TenantProvisioningHandler.cs`:

```csharp
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Provisioning;

public sealed class TenantProvisioningHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantProvisioner provisioner,
    TimeProvider timeProvider)
{
    public const string MessageType = "TenantProvisioningRequested";

    public async Task HandleAsync(TenantProvisioningRequested message, CancellationToken cancellationToken)
    {
        var tenant = await controlPlaneDbContext.Tenants.SingleOrDefaultAsync(
                         candidate => candidate.Id == message.TenantId,
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
            await provisioner.GrantTenantAccessAsync(tenant.DatabaseName, cancellationToken);

            step = TenantProvisioningStep.SeedingOwner;
            await RecordAsync(tenant, step, cancellationToken);
            await SeedOwnerAsync(tenant, message.OwnerExternalUserId, cancellationToken);

            tenant.CompleteProvisioning(timeProvider.GetUtcNow());
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            tenant.RecordProvisioningFailure(step, exception.Message, timeProvider.GetUtcNow());
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private async Task RecordAsync(Tenant tenant, TenantProvisioningStep step, CancellationToken cancellationToken)
    {
        tenant.RecordProvisioningProgress(step, timeProvider.GetUtcNow());
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedOwnerAsync(Tenant tenant, string ownerExternalUserId, CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(ownerExternalUserId);
        var ownerRole = SystemAccessCatalog.Roles.Single(role => role.Code == "owner");

        await using var tenantDbContext = provisioner.CreateTenantDbContext(tenant.DatabaseName);
        var user = await tenantDbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalUserId == externalUserId,
            cancellationToken);

        if (user is null)
        {
            user = TenantUser.Create(Guid.CreateVersion7(), externalUserId, TenantUserStatus.Active);
            tenantDbContext.Users.Add(user);
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasRole = await tenantDbContext.UserRoles.AnyAsync(
            assignment => assignment.UserId == user.Id && assignment.RoleId == ownerRole.Id,
            cancellationToken);

        if (!hasRole)
        {
            tenantDbContext.UserRoles.Add(TenantUserRole.Create(user.Id, ownerRole.Id));
            await tenantDbContext.SaveChangesAsync(cancellationToken);
        }

        var hasMembership = await controlPlaneDbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            controlPlaneDbContext.Memberships.Add(
                Membership.Create(Guid.CreateVersion7(), tenant.Id, externalUserId));
            await controlPlaneDbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 6: `TenantProvisioner`'a context fabrikası ekle**

Handler'ın tenant veritabanına provisioner credential'ı ile bağlanması gerekir.
`TenantProvisioner` sınıfına ekle:

```csharp
    public TenantDbContext CreateTenantDbContext(TenantDatabaseName databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        return new TenantDbContextFactory(connectionString).Create(databaseName.Value);
    }
```

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `TenantProvisioningHandlerTests` içindeki altı test PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): provision tenants through idempotent steps"
```

---

### Görev 6: Outbox drainer ve worker

**Files:**
- Create: `apps/api/src/Infrastructure/Messaging/OutboxDrainer.cs`
- Create: `apps/api/src/Infrastructure/Messaging/OutboxProcessor.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/OutboxDrainerTests.cs`

**Interfaces:**
- Consumes: Görev 1'in `OutboxMessage`'ı, Görev 5'in handler'ı.
- Produces: `OutboxDrainer.DrainAsync(CancellationToken)` → işlenen mesaj sayısı; `OutboxProcessor` hosted service.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/OutboxDrainerTests.cs`:

```csharp
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class OutboxDrainerTests
{
    [Fact]
    public async Task DrainAsync_ProcessesAPendingMessageAndStampsIt()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id);
        var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, processed);
        await using var context = fixture.CreateControlPlane();
        var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(TenantStatus.Active, (await fixture.ReloadAsync(tenant.Id)).Status);
    }

    [Fact]
    public async Task DrainAsync_WhenTheHandlerFails_RecordsTheAttemptAndKeepsTheMessage()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        await EnqueueAsync(fixture, Guid.NewGuid());
        var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
        await using var context = fixture.CreateControlPlane();
        var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.LastError);
        Assert.True(message.NextAttemptAt > message.CreatedAt);
    }

    [Fact]
    public async Task DrainAsync_SkipsMessagesThatExhaustedTheirAttempts()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id);

        await using (var context = fixture.CreateControlPlane())
        {
            var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);

            for (var attempt = 0; attempt < OutboxMessage.MaximumAttempts; attempt++)
            {
                message.RecordFailure("boom", DateTimeOffset.UtcNow.AddMinutes(-10));
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
        Assert.Equal(TenantStatus.Provisioning, (await fixture.ReloadAsync(tenant.Id)).Status);
    }

    [Fact]
    public async Task DrainAsync_SkipsMessagesThatAreNotDueYet()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id);

        await using (var context = fixture.CreateControlPlane())
        {
            var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
            message.RecordFailure("boom", DateTimeOffset.UtcNow.AddMinutes(10));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task DrainAsync_RunConcurrently_ProcessesEachMessageOnce()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id);

        // Act
        var results = await Task.WhenAll(
            CreateDrainer(fixture).DrainAsync(TestContext.Current.CancellationToken),
            CreateDrainer(fixture).DrainAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, results.Sum());
    }

    private static OutboxDrainer CreateDrainer(ProvisioningFixture fixture) =>
        new(
            fixture.CreateControlPlane(),
            message => new TenantProvisioningHandler(
                fixture.CreateControlPlane(),
                fixture.Provisioner,
                TimeProvider.System).HandleAsync(message, CancellationToken.None),
            TimeProvider.System);

    private static async Task EnqueueAsync(ProvisioningFixture fixture, Guid tenantId)
    {
        await using var context = fixture.CreateControlPlane();
        context.OutboxMessages.Add(OutboxMessage.Create(
            Guid.CreateVersion7(),
            TenantProvisioningHandler.MessageType,
            $$"""{"TenantId":"{{tenantId}}","OwnerExternalUserId":"{{ProvisioningFixture.OwnerExternalUserId}}"}""",
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Derleme hatası — `OutboxDrainer` bulunamıyor.

- [ ] **Step 3: `OutboxDrainer`'ı yaz**

Mesaj satırı bir transaction içinde `FOR UPDATE SKIP LOCKED` ile kilitlenir; asıl iş bu
transaction'ın dışında, handler'ın kendi bağlantısında yürür. Handler patlarsa iş geri alınır
ama deneme kaydı commit edilir.

`apps/api/src/Infrastructure/Messaging/OutboxDrainer.cs`:

```csharp
using System.Text.Json;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging;

public sealed class OutboxDrainer(
    ControlPlaneDbContext dbContext,
    Func<TenantProvisioningRequested, Task> handle,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private const int BatchSize = 10;

    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        // The row lock is held for the whole batch so a second worker skips these messages, and so
        // that a crashed process rolls back and releases them without any bookkeeping of its own.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.OutboxMessages
            .FromSql($"""
                SELECT * FROM control.outbox_messages
                WHERE processed_at IS NULL
                  AND attempt_count < {OutboxMessage.MaximumAttempts}
                  AND next_attempt_at <= {timeProvider.GetUtcNow()}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        var processed = 0;

        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<TenantProvisioningRequested>(message.Payload)
                              ?? throw new InvalidOperationException(
                                  $"Outbox message '{message.Id}' has an empty payload.");
                await handle(payload);
                message.MarkProcessed(timeProvider.GetUtcNow());
                processed++;
            }
            catch (Exception exception)
            {
                message.RecordFailure(exception.Message, timeProvider.GetUtcNow());
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return processed;
    }

    public ValueTask DisposeAsync() => dbContext.DisposeAsync();
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `OutboxDrainerTests` içindeki beş test PASS.

- [ ] **Step 5: Hosted service'i yaz**

`apps/api/src/Infrastructure/Messaging/OutboxProcessor.cs`:

```csharp
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provider = scope.ServiceProvider;
                await using var drainer = new OutboxDrainer(
                    provider.GetRequiredService<ControlPlaneDbContext>(),
                    message => provider.GetRequiredService<TenantProvisioningHandler>()
                        .HandleAsync(message, stoppingToken),
                    timeProvider);
                await drainer.DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox drain failed.");
            }

            try
            {
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

- [ ] **Step 6: Servisleri kaydet**

`PostgresServiceCollectionExtensions.AddTenantPersistence` içine, `return services;` satırından önce:

```csharp
        services.AddScoped<TenantProvisioningHandler>();
```

`apps/api/src/Api/Program.cs` içine, `builder.Services.AddScoped<TenantResolver>();` satırından sonra:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<OutboxProcessor>();
```

`Program.cs` başına `using Infrastructure.Messaging;` ekle.

Handler'ın `ControlPlaneDbContext` ve `TenantProvisioner` bağımlılıkları zaten kayıtlı;
`TimeProvider` kaydı burada ekleniyor.

- [ ] **Step 7: Tüm testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 8: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): drain the outbox from a background worker"
```

---

### Görev 7: Tenant oluşturma, detay ve yeniden deneme endpoint'leri

**Files:**
- Create: `apps/api/src/ControlPlane/TenantEndpoints.cs`
- Modify: `apps/api/src/ControlPlane/ControlPlaneEndpoints.cs`
- Modify: `apps/api/AGENTS.md`
- Test: `apps/api/tests/IntegrationTests/TenantProvisioningEndpointTests.cs`

**Interfaces:**
- Consumes: Görev 1'in `OutboxMessage`'ı, Görev 5'in `TenantProvisioningHandler.MessageType` sabiti.
- Produces: `POST /admin/api/v1/tenants`, `GET /admin/api/v1/tenants/{id}`,
  `POST /admin/api/v1/tenants/{id}/retry-provisioning`.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/TenantProvisioningEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisioningEndpointTests
{
    [Fact]
    public async Task PostTenant_QueuesProvisioningAndReturnsAccepted()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Provisioning", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostTenant_WritesTheTenantAndTheMessageTogether()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await fixture.CountControlPlaneRowsAsync("control.tenants"));
        Assert.Equal(1, await fixture.CountControlPlaneRowsAsync("control.outbox_messages"));
    }

    [Fact]
    public async Task PostTenant_ForADuplicateAlias_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var first = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostTenant_ForAReservedAlias_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "admin" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTenant_ForANonPlatformAdmin_ReturnsForbidden()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenant_ReportsProvisioningProgress()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var created = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        var location = created.Headers.Location!.ToString();

        // Act
        using var response = await fixture.Client.GetAsync(location, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("acme", body.GetProperty("alias").GetString());
        Assert.Equal("CreatingDatabase", body.GetProperty("provisioningStep").GetString());
    }

    [Fact]
    public async Task GetTenant_ForAnUnknownIdentifier_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.GetAsync(
            $"/admin/api/v1/tenants/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetryProvisioning_QueuesAnotherMessage()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);
        using var created = await fixture.Client.PostAsJsonAsync(
            "/admin/api/v1/tenants",
            new { alias = "acme" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();

        // Act
        using var response = await fixture.Client.PostAsync(
            $"/admin/api/v1/tenants/{id}/retry-provisioning",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Testler 404 alarak FAIL — endpoint'ler yok.

- [ ] **Step 3: Endpoint'leri yaz**

Tenant satırı ve outbox mesajı tek bir `SaveChangesAsync` çağrısıyla, yani tek transaction'da
yazılır.

`apps/api/src/ControlPlane/TenantEndpoints.cs`:

```csharp
using System.Security.Claims;
using System.Text.Json;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane;

public sealed record CreateTenantRequest(string Alias);

public sealed record TenantDetail(
    Guid Id,
    string Alias,
    string Status,
    string? ProvisioningStep,
    string? ProvisioningError,
    DateTimeOffset CreatedAt);

public static class TenantEndpoints
{
    public static void MapTenants(this RouteGroupBuilder group)
    {
        group.MapPost("/tenants", CreateAsync);
        group.MapGet("/tenants/{id:guid}", GetAsync).WithName(nameof(GetAsync));
        group.MapPost("/tenants/{id:guid}/retry-provisioning", RetryAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateTenantRequest request,
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        TenantAlias alias;

        try
        {
            alias = TenantAlias.Create(request.Alias);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        if (await dbContext.Tenants.AnyAsync(tenant => tenant.Alias == alias, cancellationToken))
        {
            return Results.Conflict(new { error = $"Alias '{request.Alias}' is already taken." });
        }

        var ownerExternalUserId = user.FindFirstValue("sub")!;
        var now = timeProvider.GetUtcNow();
        var tenant = Tenant.Create(Guid.CreateVersion7(), alias, now);
        dbContext.Tenants.Add(tenant);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            Guid.CreateVersion7(),
            TenantProvisioningHandler.MessageType,
            JsonSerializer.Serialize(new TenantProvisioningRequested(tenant.Id, ownerExternalUserId)),
            now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.AcceptedAtRoute(
            nameof(GetAsync),
            new { id = tenant.Id },
            ToDetail(tenant));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ControlPlaneDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return tenant is null ? Results.NotFound() : Results.Ok(ToDetail(tenant));
    }

    private static async Task<IResult> RetryAsync(
        Guid id,
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (tenant is null)
        {
            return Results.NotFound();
        }

        if (tenant.Status is not TenantStatus.Provisioning)
        {
            return Results.Conflict(new { error = $"Tenant is {tenant.Status}, not Provisioning." });
        }

        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            Guid.CreateVersion7(),
            TenantProvisioningHandler.MessageType,
            JsonSerializer.Serialize(new TenantProvisioningRequested(
                tenant.Id,
                user.FindFirstValue("sub")!)),
            timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.AcceptedAtRoute(nameof(GetAsync), new { id = tenant.Id }, ToDetail(tenant));
    }

    private static TenantDetail ToDetail(Tenant tenant) => new(
        tenant.Id,
        tenant.Alias.Value,
        tenant.Status.ToString(),
        tenant.ProvisioningStep?.ToString(),
        tenant.ProvisioningError,
        tenant.CreatedAt);
}
```

- [ ] **Step 4: Fixture'a satır sayma yardımcısı ekle**

`apps/api/tests/IntegrationTests/ControlPlaneFixture.cs` sınıfına ekle:

```csharp
    public async Task<long> CountControlPlaneRowsAsync(string qualifiedTable)
    {
        await using var connection = new NpgsqlConnection(controlPlaneConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {qualifiedTable}", connection);

        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
```

Sınıfa `private readonly string controlPlaneConnectionString;` alanını ekle ve kurucuda doldur.

- [ ] **Step 5: Grubu bağla**

`apps/api/src/ControlPlane/ControlPlaneEndpoints.cs` içindeki `MapControlPlane` metodunda,
`group.MapGet("/tenants", ...)` çağrısından sonra ekle:

```csharp
        group.MapTenants();
```

- [ ] **Step 6: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS.

- [ ] **Step 7: `AGENTS.md`'yi güncelle**

Kurallar listesine ekle:

```markdown
- Queue provisioning work through the control plane outbox in the same transaction as the business change; provisioning steps must be safe to run again.
```

- [ ] **Step 8: OpenAPI istemcisini yeniden üret**

```bash
cd apps/api && dotnet build src/Api/Api.csproj
```

Ardından repo kökünde:

```bash
pnpm --filter @st/api-client build
```

- [ ] **Step 9: Commit**

```bash
git add -A apps/api packages/api-client
git commit -m "feat(api): create tenants through the control plane"
```

---

## Tamamlanma Kontrolü

2026-09-10 tarihinde temiz bir ortamda doğrulandı:

- [x] `dotnet test --solution apps/api/Api.slnx` — dört proje, 108 test yeşil
- [x] `docker compose down -v` sonrası sıra çalışıyor: bootstrap rolleri → `migrate control-plane` →
      grant script'i → API başlıyor
- [x] Kuyruğa bırakılan bir provisioning mesajı worker tarafından işlendi: tenant
      `Provisioning` → `Active`, `provisioning_step` temizlendi, hata yok, mesaj `processed_at`
      damgası aldı
- [x] Oluşan tenant veritabanında şema migrate edilmiş ve sistem kataloğu seed edilmiş:
      `users=1`, `user_roles=1`, `roles=1`, `permissions=5`, `role_permissions=5`;
      owner kullanıcısı `owner` rolüne sahip
- [x] `st_tenant` kendi tenant veritabanını okuyabiliyor, `control.tenants`'a yazmaya
      çalıştığında `permission denied` alıyor — sınır gerçek credential'larla da tutuyor
- [x] `migrate tenants`, `st_provisioner`'ın sahip olduğu tabloları `st_migrator` ile migrate
      edebiliyor (`GRANT st_provisioner TO st_migrator` sayesinde)
- [x] `apps/api/AGENTS.md` outbox kuralını içeriyor

HTTP seviyesindeki uçtan uca akış (`POST /admin/api/v1/tenants` → poll → `Active`) elle
çalıştırılmadı, çünkü control plane endpoint'leri Clerk token'ı gerektiriyor ve `Clerk:Issuer`
lokalde yapılandırılmamış durumda. Endpoint'lerin kendisi entegrasyon testlerinde test
kimlik doğrulama şemasıyla kanıtlanıyor; yukarıdaki doğrulama aynı zinciri outbox mesajından
başlatarak gerçek uygulama process'i ve gerçek credential'larla yürütüyor.
