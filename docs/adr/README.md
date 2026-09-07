# Architecture Decision Records

Bu dizin, `apps/api` (.NET backend) için alınan mimari kararları kayıt
altına alan Architecture Decision Record (ADR) setini içerir. Pratik,
Andrew Harmel-Law'ın "Facilitating Software Architecture" kitabındaki
yaklaşımdan geliyor: her karar, neden verildiği ve hangi alternatiflerin
elendiğiyle birlikte, tek bir dosyada, kalıcı olarak kayıt altına
alınır. Bir kararı değiştirmek, mevcut ADR'yi silmek değil, onu
`Superseded` olarak işaretleyip yeni bir ADR eklemek anlamına gelir.

ADR'ler, bir kararı okuyanın hem "ne karar verildi" hem de "neden bu ve
neden diğer seçenekler değil" sorularına cevap bulabilmesi için sabit
bir şablon izler: Context / Decision / Consequences / Alternatives
Considered.

## Tenancy & Access Control

- [0001. Tenant İzolasyonu: Schema-per-Tenant](0001-tenant-isolation-schema-per-tenant.md) — Row-level `tenant_id` yerine tek Postgres instance içinde tenant başına ayrı schema.
- [0002. Control Plane: Paylaşımlı `admin` Schema](0002-control-plane-admin-schema.md) — Tenant kaydı, membership, invitation tüm tenant'lar arasında paylaşımlı `admin` schema'sında.
- [0003. Auth/Authorization Ayrımı](0003-auth-authorization-split.md) — Clerk sadece kimlik doğrulama için, tenant/rol modeli tamamen kendi `admin` schema'sında.
- [0004. Tenant Çözümleme: Path Bazlı](0004-path-based-tenant-resolution.md) — `/{tenant-alias}/api/...`, subdomain veya header değil, mobil uyumluluğu için.
- [0005. Membership & Davet](0005-membership-and-invitation.md) — Çoklu-tenant üyelik modeli, workspace switcher.
- [0006. Permission Sistemi](0006-permission-system.md) — Native ASP.NET Core policy-based authorization, `[RequiresPermission]` pipeline behavior.

## Domain & Application Layer

- [0007. Domain Modelleme: Building Blocks](0007-domain-modeling-building-blocks.md) — `Entity`/`AggregateRoot`/`ValueObject`/`DomainEvent`, DDD terminolojisi.
- [0008. Dual ID Deseni](0008-dual-id-pattern.md) — İç Guid PK/FK + tenant-schema-bazlı Postgres `SEQUENCE`'tan üretilen okunabilir public ID.
- [0009. CQRS-lite](0009-cqrs-lite.md) — Ayrı command/query sınıfları, aynı veritabanına karşı; full CQRS/event sourcing değil.
- [0010. Mediator/Pipeline: MediatR Değil](0010-hand-rolled-mediator-pipeline.md) — Elle yazılmış mediator + pipeline behavior'lar; EF Core `DbContext` zaten Unit of Work.
- [0011. Result Pattern](0011-result-pattern.md) — Beklenen iş hatalarında exception değil `Result<T>`.

## Data & Persistence

- [0012. ORM: EF Core + Dapper](0012-orm-ef-core-and-dapper.md) — EF Core birincil, Dapper performans-kritik ham sorgular için.
- [0013. Tenant Middleware: Elle Yazıldı](0013-hand-rolled-tenant-middleware.md) — Finbuckle.MultiTenant gibi hazır kütüphane değil, güvenlik kritik katman kontrol için elle yazıldı.
- [0014. Migration: Schema-per-Tenant](0014-schema-per-tenant-migrations.md) — EF Core code-first, deploy sırasında tüm tenant şemalarına dinamik uygulama.
- [0015. Concurrency Kontrolü](0015-concurrency-control.md) — Yüksek çakışmada pessimistic (`FOR UPDATE`), düşük çakışmada optimistic (RowVersion).
- [0016. Timezone: UTC Politikası](0016-utc-timezone-policy.md) — Backend/DB her zaman UTC, kullanıcı IANA timezone'u ayrı saklanır.
- [0017. Caching Stratejisi](0017-caching-strategy.md) — Genel katman yok, tenant-resolution için `IMemoryCache`, `ICachedQuery` marker hazır.
- [0018. Outbox Pattern](0018-outbox-pattern.md) — Aynı transaction'da outbox mesajı, `BackgroundService` + `SKIP LOCKED` ile işleme.
- [0019. Temporal: Şimdilik Eklenmedi](0019-no-temporal-yet.md) — Gerçek çok-adımlı/dayanıklı workflow ihtiyacı yok.
- [0020. Quartz.NET: Şimdilik Gerekmiyor](0020-no-quartz-yet.md) — Outbox'ın ihtiyacı cron değil, sürekli kuyruk boşaltma.

## API & Integration

- [0021. API Stili: REST, `/api/v1`](0021-rest-api-versioning.md) — GraphQL/tRPC değil, additive-only versiyonlama.
- [0022. Ortak Client: Generated TypeScript](0022-generated-typescript-client.md) — OpenAPI'den `packages/api-client`'a generate edilen tek client.
- [0023. API Yüzeyi: Tek Ortak Yüzey](0023-shared-api-surface.md) — web/mobile/admin başta aynı yüzeyi paylaşır, BFF ayrımı sadece UX gerçekten ayrışırsa.
- [0024. Real-time: SignalR](0024-signalr-realtime.md) — Native .NET real-time kütüphanesi, Socket.IO değil.

## Engineering Practices & Platform

- [0025. Test Stratejisi](0025-test-strategy.md) — xUnit + Testcontainers (gerçek Postgres), day 1'den itibaren, ayrı `TenantIsolationTests` paketi.
- [0026. Logging: Serilog + Seq](0026-structured-logging.md) — Otomatik tenant_id/request_id/user_id enrichment.
- [0027. Kod Standardı](0027-code-standard.md) — Gereksiz comment yok (AAA istisnası hariç), DDD vocabulary, ADR pratiği.
- [0028. Backend Runtime: .NET 10 (LTS)](0028-dotnet-10-runtime.md) — 3 yıl destekli güncel LTS, kısa ömürlü STS değil.
- [0029. Monorepo Tooling](0029-monorepo-tooling.md) — Turborepo TS tarafını yönetir, `apps/api`'de ince `package.json` wrapper'ı ile `pnpm dev` entegrasyonu.
