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
aynı disiplin.

**Sonradan güncelleme (as-built notu):** İlk tasarımda demo command
olarak Faz 2b'nin `TenantProvisioningService`'ini saran bir
`ProvisionTenantCommand` planlanmıştı. Uygulama sırasında bundan
vazgeçildi: `TenantProvisioningService.ProvisionAsync` kendi
transaction'ını (`BeginTransactionAsync`/`SaveChangesAsync`/
`CommitAsync`) kendi içinde yönetiyor — bu, pipeline'ın
`SaveChangesUnitOfWorkBehavior`'ının transaction/outbox atomicity
sahipliğiyle çakışırdı (aynı SaveChanges çağrısında hem iş
değişikliğinin hem outbox mesajının yazılması garantisi bozulurdu).
Bunun yerine gerçek, sahte olmayan başka bir yetenek seçildi:
`RenameTenantCommand` — `Tenant.Rename(string)` domain metodunu
(bu fazda eklendi) EF Core'un normal tracked-entity mekanizmasıyla
çağıran, saf bir command. Ayrıca handler'ın `Api.Application`'da
kalıp `AdminDbContext`'e (EF Core) doğrudan bağımlı olmaması için
`ITenantRepository` arayüzü (Application'da tanımlı, Infrastructure'da
`TenantRepository` ile implemente edilir) eklendi — bu, Faz 3'ün
final review'ının "Application katmanı EF Core'dan bağımsız kalsın"
kararını korur.

## Pipeline sırası

`Logging (en dışta) → Permission → Validation (FluentValidation) →
Handler → SaveChanges (Unit of Work)`. Auth/Tenant/Membership zaten
HTTP katmanında (Faz 3) çözüldüğü için mediator pipeline'ı Permission'dan
başlar.

**Sonradan güncelleme:** İlk uygulamada Validation, Permission'dan önce
kaydedilmişti (ADR 0006'nın sırasının tersi) — final review bunu
yakaladı (hem ADR'ye aykırıydı hem de yetkisiz bir çağıranın validation
hata detaylarını, permission reddinden önce görebilmesi gibi küçük bir
bilgi sızıntısı riski taşıyordu) ve ADR 0006 ile birebir eşleşecek
şekilde (`Permission → Validation`) düzeltildi.

## Eklenecek dosyalar (üst düzey)

`Api.Application/`:
- `Result.cs` — `Result` / `Result<T>`, `Error` (kod + mesaj).
- `IRequest.cs`, `IRequest{TResponse}.cs`, `IRequestHandler.cs`,
  `IMediator.cs`, `Mediator.cs` — hand-rolled mediator.
- `IPipelineBehavior.cs`, `LoggingBehavior.cs`, `ValidationBehavior.cs`
  (FluentValidation), `PermissionBehavior.cs` (Faz 3'ün `"permission"`
  claim'lerini `IHttpContextAccessor` üzerinden okur).
- `Tenants/RenameTenantCommand.cs` + handler + validator + `ITenantRepository`
  — `Tenant.Rename(string)`'i mediator arkasına sarar (bkz. yukarıdaki
  as-built notu).
- `RequiresPermissionAttribute.cs` — command/query'lere permission
  string'i bağlamak için.
- `ITenantScopedRequest.cs` — final review sonrası eklendi (bkz. Kabul
  kriterleri altındaki not): bir command'ın hedef aldığı tenant ile
  çağıranın authenticated `tenant_id` claim'inin eşleştiğini
  `PermissionBehavior`'a doğrulatmak için.

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
  mekanizması bu fazda `RenameTenantCommand`'ın ürettiği
  `TenantRenamedDomainEvent`'i taşıyacak şekilde kurulur, gerçek bir
  tüketici (dispatcher) henüz yok — `OutboxProcessor` mesajı sadece
  "processed" işaretler, kapsam dışı iş sonradan eklenecek.

## Kabul kriterleri

1. `IMediator.Send(new RenameTenantCommand(...))` gerçek bir `Tenant`
   satırını EF Core'un tracked-entity mekanizmasıyla günceller
   (as-built notu: artık `ProvisionTenantCommand`/schema oluşturma
   değil, `RenameTenantCommand`).
2. Geçersiz input (`RenameTenantCommand` boş isimle) `ValidationBehavior`
   tarafından yakalanıp `Result` başarısızlığı olarak dönüyor,
   handler'a hiç ulaşmıyor.
3. Gerekli permission'a sahip olmayan bir `ClaimsPrincipal` ile
   çağrıldığında `PermissionBehavior` isteği reddediyor. Ayrıca
   (final review sonrası eklendi): permission'a sahip ama BAŞKA bir
   tenant için (`ITenantScopedRequest.TenantId` ≠ çağıranın `tenant_id`
   claim'i) çağrıldığında da reddediyor — cross-tenant yazma riski
   kapatıldı.
4. Command başarıyla işlendiğinde, aynı `SaveChanges` çağrısında bir
   `OutboxMessage` satırı yazılıyor — bu, gerçek DI-wired mediator
   pipeline'ı üzerinden (bir test'in kendi kopyaladığı mantıkla değil)
   doğrulanıyor.
5. `OutboxProcessor` (BackgroundService), bekleyen outbox mesajlarını
   `SELECT ... FOR UPDATE SKIP LOCKED` ile işleyip "processed" olarak
   işaretliyor; iki instance'ın aynı mesajı iki kez işlemediği
   kanıtlanıyor; bu, `OutboxProcessor`'ın gerçek production kodu
   çağrılarak doğrulanıyor.
6. `pnpm --filter api build` ve `pnpm --filter api test` başarılı.
