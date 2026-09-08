# apps/api Phase 2b: Persistence foundation

Date: 2026-09-08
Status: approved (autonomous run, no interactive review — see phase ledger)

**Sonradan güncelleme (as-built notu):** Uygulama sırasında `ApiDbContext`
adı `AdminDbContext` olarak değiştirildi (tenant-scoped `TenantDbContext`
ile isim simetrisi için — bkz. Kabul kriterleri sonrası eklenen dosyalar).
Final review sonrası ayrıca şunlar eklendi: `SafePostgresIdentifier`
(paylaşımlı raw-SQL identifier doğrulama), `Configurations/Admin/` alt
namespace'i (assembly-wide config scan riskini kapatmak için),
`Memberships`/`Invitations` üzerinde shadow foreign key'ler (yeni bir
migration ile), ve `TenantSlug`'ın maksimum uzunluğu 63'ten 56'ya indi
(`"tenant_"` prefix'iyle toplamda 63 byte Postgres limitini aşmamak
için). Detaylar için `docs/superpowers/plans/2026-09-07-apps-api-phase2b-persistence.md`
ve commit geçmişi.

## Amaç

EF Core + Npgsql ile gerçek bir Postgres'e karşı çalışan persistence
katmanı kurar: `admin` schema'sındaki control-plane tabloları
([ADR 0002](../../adr/0002-control-plane-admin-schema.md)), tenant
provisioning (yeni tenant için Postgres schema'sı oluşturma —
[ADR 0001](../../adr/0001-tenant-isolation-schema-per-tenant.md)),
tenant başına migration uygulama
([ADR 0014](../../adr/0014-schema-per-tenant-migrations.md)), dual ID
sequence üretimi ([ADR 0008](../../adr/0008-dual-id-pattern.md)), UTC
zaman damgası ve concurrency konvansiyonları
([ADR 0015](../../adr/0015-concurrency-control.md),
[ADR 0016](../../adr/0016-utc-timezone-policy.md)).
[ADR 0025](../../adr/0025-test-strategy.md) gereği tüm testler gerçek
Postgres'e karşı Testcontainers ile çalışır — bu, projenin Testcontainers
kullanan ilk fazı.

## Kapsam kararı: sahte iş varlığı uydurulmuyor

Bu starter kit'in ürünü henüz belirlenmedi — hiçbir tenant-schema iş
varlığı (invoice, ledger, sipariş vb.) yok. Dual ID pattern, optimistic/
pessimistic concurrency gibi desenler ADR'lerde gerçek iş varlıklarına
uygulanacak şekilde tarif edilmiş. Bu fazda bu desenleri göstermek için
sahte bir iş varlığı (örn. uydurma bir "Invoice") eklemek,
AGENTS.md'nin "no speculative content" ilkesini ihlal eder — ürünün
kendisi henüz tanımlanmamışken varsayımsal bir domain kavramı icat
etmek olur. Bunun yerine:

- **Dual ID (ADR 0008)**: genel amaçlı, yeniden kullanılabilir bir
  `TenantSequenceIdGenerator` servisi olarak uygulanır — belirli bir
  tenant schema'sında adlandırılmış bir Postgres `SEQUENCE`'tan
  biçimlendirilmiş ID üretir (örn. `PREFIX-000001`). Gerçek bir iş
  varlığı ortaya çıktığında bu servis doğrudan kullanılır.
- **Optimistic concurrency (ADR 0015)**: `Membership` üzerinde
  gerçek, var olan bir "düşük çakışma/kullanıcı düzenleme" senaryosu
  olan rol değişikliğinde `RowVersion` ile gösterilir — bu uydurma
  değil, ADR 0005'in zaten tanımladığı gerçek bir admin-schema
  varlığı.
- **Pessimistic locking (ADR 0015)**: bilinçli olarak bu fazın dışında
  bırakılıyor — ledger/balance gibi yüksek çakışmalı bir gerçek iş
  senaryosu bu starter kit'te henüz yok. İlk finansal/yüksek-çakışma
  özelliği eklendiğinde `SELECT ... FOR UPDATE` o zaman uygulanacak.
  Bu bir eksiklik değil, kayıtlı bir kapsam kararı.

## Eklenecek/değişecek dosyalar (üst düzey)

`apps/api/src/Api.Infrastructure/`:
- `ApiDbContext.cs` — `HasDefaultSchema("admin")`, `DbSet<Tenant>`,
  `DbSet<User>`, `DbSet<Membership>`, `DbSet<Invitation>`,
  `DbSet<RolePermission>`.
- `Configurations/` — her entity için `IEntityTypeConfiguration<T>`.
- `TenantSequenceIdGenerator.cs` — ADR 0008.
- `TenantProvisioningService.cs` — yeni tenant için Postgres schema'sı
  oluşturma (ADR 0001).
- `TenantMigrationRunner.cs` — `admin.tenants` listesini gezip her
  tenant schema'sına migration uygulama (ADR 0014).
- `AuditableSaveChangesInterceptor.cs` — `CreatedAtUtc`/`UpdatedAtUtc`
  otomatik doldurma (ADR 0016).
- `Migrations/` — EF Core code-first migration'lar (admin schema).

`apps/api/src/Api.Domain/`:
- `Tenant.cs`, `User.cs`, `Membership.cs`, `Invitation.cs`,
  `RolePermission.cs` — `AggregateRoot<Guid>`'den türeyen entity'ler.
  `IAuditable` marker interface (`CreatedAtUtc`/`UpdatedAtUtc`).
  `Membership` ayrıca `RowVersion` (byte[], EF concurrency token) taşır.

`apps/api/tests/Api.Tests.Integration/` (yeni proje — ADR 0025'in
gerçek Postgres gerektiren testleri için; `Api.Tests.Unit`'ten ayrı,
isimlendirme ADR 0025'in "isimli, ayrı paket" ilkesiyle uyumlu):
- Testcontainers ile gerçek Postgres ayağa kaldıran bir
  `PostgresContainerFixture` (`IAsyncLifetime`, `ICollectionFixture`).
- `ApiDbContextTests.cs`, `TenantProvisioningServiceTests.cs`,
  `TenantSequenceIdGeneratorTests.cs`, `TenantMigrationRunnerTests.cs`,
  `MembershipConcurrencyTests.cs`, `AuditableInterceptorTests.cs`.

## Bağımlılıklar

- `Npgsql.EntityFrameworkCore.PostgreSQL` (Api.Infrastructure)
- `Microsoft.EntityFrameworkCore.Design` (migration tooling, Api.Host
  veya Api.Infrastructure — dotnet-ef aracı için)
- `Testcontainers.PostgreSql` (Api.Tests.Integration)
- `Microsoft.AspNetCore.Mvc.Testing` zaten Api.Tests.Unit'te var,
  Api.Tests.Integration'a gerek yok (bu proje DbContext'i doğrudan
  test ediyor, HTTP host'u değil).

## Kapsam dışı

- Gerçek tenant-schema iş varlıkları (henüz ürün yok).
- Pessimistic locking (yukarıda gerekçelendirildi, ileri faza).
- Dapper (ADR 0012 ikisini de öngörüyor ama bu fazda karmaşık/performans
  kritik bir sorgu ihtiyacı yok; EF Core yeterli — Dapper gerçek bir
  ihtiyaç çıkınca eklenir).
- Auth/tenant middleware (Faz 3).
- Outbox (Faz 4).

## Kabul kriterleri

1. `apps/api/tests/Api.Tests.Integration` gerçek bir Postgres
   container'ı ayağa kaldırıp `ApiDbContext`'in migration'ını
   uygulayabiliyor; `admin` schema'sında 5 tablo oluşuyor.
2. Yeni bir `Tenant` oluşturulduğunda karşılık gelen Postgres schema'sı
   gerçekten var oluyor (`TenantProvisioningServiceTests`).
3. `TenantSequenceIdGenerator`, bir tenant schema'sında art arda
   çağrıldığında sıralı, formatlı ID'ler üretiyor
   (`TenantSequenceIdGeneratorTests`).
4. `TenantMigrationRunner`, `admin.tenants`'taki her tenant için ilgili
   schema'ya migration uyguluyor (`TenantMigrationRunnerTests`).
5. İki `ApiDbContext` aynı `Membership`'i yükleyip biri kaydettikten
   sonra diğeri kaydetmeye çalıştığında `DbUpdateConcurrencyException`
   fırlıyor (`MembershipConcurrencyTests`).
6. Yeni oluşturulan bir entity'nin `CreatedAtUtc`/`UpdatedAtUtc`'si
   client'tan gelmiyor, backend tarafından `DateTimeOffset.UtcNow` ile
   üretiliyor (`AuditableInterceptorTests`).
7. `pnpm --filter api build` ve `pnpm --filter api test` başarılı
   (Testcontainers testleri `turbo.json`'da `cache: false` altında,
   Faz 1'in bulgusuna göre — gerçek DB testleri asla cache'lenmemeli).
