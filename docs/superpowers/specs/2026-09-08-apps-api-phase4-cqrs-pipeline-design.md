# apps/api Phase 4: CQRS-lite + Mediator/Pipeline + Outbox

Date: 2026-09-08
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

`Api.Application` katmanına ilk gerçek içeriği ekler: hand-rolled
mediator + pipeline behavior'lar
([ADR 0010](../../adr/0010-hand-rolled-mediator-pipeline.md)), CQRS-lite
command/query ayrımı ([ADR 0009](../../adr/0009-cqrs-lite.md)),
`Result<T>` pattern ([ADR 0011](../../adr/0011-result-pattern.md)), ve
outbox pattern ([ADR 0018](../../adr/0018-outbox-pattern.md)). Faz
3'ün ürettiği `"permission"` claim mekanizması, ADR 0006'nın asıl
öngördüğü şekilde bir permission pipeline behavior'ında da kullanılır.

## Kapsam kararı: gerçek endpoint yok, mediator doğrudan test ediliyor

API yüzeyi (REST endpoint'leri, OpenAPI) Faz 5'in işi. Bu faz mediator
+ pipeline'ı HTTP'nin üzerinden değil, doğrudan `IMediator.Send(...)`
çağrılarıyla test eder — Faz 2b'nin servisleri HTTP'siz test etmesiyle
aynı disiplin. Gerçek bir command/query göstermek için sahte bir iş
kavramı uydurmak yerine, Faz 2b'nin zaten var olan
`TenantProvisioningService`'ini bir `ProvisionTenantCommand` + handler
ile sarmalıyoruz — bu gerçek, zaten ihtiyaç duyulan bir yetenek,
sadece mediator'ün arkasına taşınıyor.

## Pipeline sırası (ADR 0006 ile birebir)

`Validation (FluentValidation) → Permission → Handler → SaveChanges
(Unit of Work)`. Logging behavior tüm zincirin dışını sarar (en dışta).
Auth/Tenant/Membership zaten HTTP katmanında (Faz 3) çözüldüğü için
mediator pipeline'ı sadece Validation'dan başlar.

## Eklenecek dosyalar (üst düzey)

`Api.Application/`:
- `Result.cs` — `Result` / `Result<T>`, `Error` (kod + mesaj).
- `IRequest.cs`, `IRequest{TResponse}.cs`, `IRequestHandler.cs`,
  `IMediator.cs`, `Mediator.cs` — hand-rolled mediator.
- `IPipelineBehavior.cs`, `LoggingBehavior.cs`, `ValidationBehavior.cs`
  (FluentValidation), `PermissionBehavior.cs` (Faz 3'ün `"permission"`
  claim'lerini `IHttpContextAccessor` üzerinden okur).
- `Tenants/ProvisionTenantCommand.cs` + handler + validator —
  `TenantProvisioningService`'i mediator arkasına sarar.
- `RequiresPermissionAttribute.cs` — command/query'lere permission
  string'i bağlamak için.

`Api.Infrastructure/`:
- `OutboxMessage.cs` (Domain) + EF config + migration.
- `OutboxProcessor.cs` — `BackgroundService`, 5-10 saniyede bir
  `SELECT ... FOR UPDATE SKIP LOCKED`.
- `SaveChangesUnitOfWorkBehavior.cs` — handler sonrası `SaveChangesAsync`
  çağıran pipeline behavior (ayrı bir `UnitOfWork` sınıfı yok, ADR
  0010'un gerekçesiyle).

`Api.Host/`:
- `Program.cs`: mediator/behavior'ları DI'a kaydeder, `OutboxProcessor`'ı
  hosted service olarak ekler, `IHttpContextAccessor` kaydeder.

## Bağımlılıklar

- `FluentValidation` (Api.Application).

## Kapsam dışı

- Gerçek HTTP endpoint'leri (Faz 5).
- Serilog/Seq entegrasyonu — `LoggingBehavior` şimdilik `ILogger<T>`
  (yerleşik .NET logging) kullanır, Faz 6 Serilog'a geçirir
  ([ADR 0026](../../adr/0026-structured-logging.md)).
- Gerçek domain event tüketen bir outbox mesajı içeriği — outbox
  mekanizması bu fazda `ProvisionTenantCommand`'ın ürettiği (varsa)
  domain event'leri taşıyacak şekilde kurulur, ama event'in taşıdığı
  veri yine spekülatif bir iş özelliği icat etmeden minimal tutulur
  (örn. `TenantProvisionedDomainEvent { TenantId, SchemaName }`).

## Kabul kriterleri

1. `IMediator.Send(new ProvisionTenantCommand(...))` gerçek bir
   `Tenant` + Postgres schema'sı oluşturuyor (Faz 2b'nin servisini
   sarmalıyor).
2. Geçersiz input (`ProvisionTenantCommand` boş isimle) `ValidationBehavior`
   tarafından yakalanıp `Result` başarısızlığı olarak dönüyor,
   handler'a hiç ulaşmıyor.
3. Gerekli permission'a sahip olmayan bir `ClaimsPrincipal` ile
   çağrıldığında `PermissionBehavior` isteği reddediyor.
4. Command başarıyla işlendiğinde, aynı transaction içinde bir
   `OutboxMessage` satırı yazılıyor.
5. `OutboxProcessor` (BackgroundService), bekleyen outbox mesajlarını
   `SELECT ... FOR UPDATE SKIP LOCKED` ile işleyip "processed" olarak
   işaretliyor; iki instance'ın aynı mesajı iki kez işlemediği
   kanıtlanıyor.
6. `pnpm --filter api build` ve `pnpm --filter api test` başarılı.
