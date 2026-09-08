# apps/api Phase 5: API Surface

Date: 2026-09-08
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

Faz 4'ün mediator pipeline'ını gerçek bir HTTP endpoint'ine bağlar
([ADR 0021](../../adr/0021-rest-api-versioning.md) — REST, `/api/v1`),
ASP.NET Core'dan OpenAPI şeması üretir ve `packages/api-client`'a
TypeScript client olarak generate eder
([ADR 0022](../../adr/0022-generated-typescript-client.md)), ve SignalR
ekler ([ADR 0024](../../adr/0024-signalr-realtime.md)).
`packages/api-client` şu an tamamen boş (`.gitkeep` dışında).

## Kapsam kararı: `RenameTenantCommand`'ı gerçek endpoint'e bağlamak

Faz 4'ün `RenameTenantCommand`'ı ilk kez gerçek bir HTTP isteğinden
tetiklenecek. Yeni bir iş özelliği icat edilmiyor — zaten var olan
command'ın önüne bir REST endpoint konuyor. ADR 0004'ün kanonik URL
şekli (`/{tenant-alias}/api/v1/...`, Faz 3'te netleştirildi) korunuyor:
`PUT /{tenant-alias}/api/v1/tenants/{tenantId}`.

## Result → HTTP status kod eşlemesi (ADR 0011)

Bu fazda ilk kez somutlaşıyor:
- `Result.IsSuccess` → 200 (veya body yoksa 204).
- `Error.Code` prefix'ine göre: `"Validation.*"` → 400,
  `"Permission.Denied"` / `"Tenant.Mismatch"` → 403,
  `"*.NotFound"` → 404, bilinmeyen kod → 500 (beklenmeyen, loglanır).

## TypeScript client codegen aracı

`openapi-typescript` (tip üretimi) + `openapi-fetch` (ince, tip-güvenli
runtime client) — ikisi de saf npm paketi, Java/dotnet-tool gibi
harici bir codegen sunucusu gerektirmiyor, pnpm monorepo'suna doğal
uyuyor. `packages/api-client`'ın `build` script'i, `apps/api` build
sırasında üretilen OpenAPI JSON dosyasını okuyup tipleri üretir.

## Eklenecek dosyalar (üst düzey)

`Api.Host/`:
- `Program.cs`: `AddOpenApi()`, `MapOpenApi()`, gerçek `PUT
  /{tenant-alias}/api/v1/tenants/{tenantId}` endpoint'i (Faz 4'ün
  `RenameTenantCommand`'ını çağırır, `Result`'ı HTTP'ye çevirir),
  SignalR (`AddSignalR()`, `MapHub<...>`).
- `ResultHttpMapper.cs` — `Result`/`Result<T>` → `IResult` (ASP.NET
  Core minimal API sonucu) eşleme mantığı.
- `Hubs/TenantHub.cs` — minimal, tanı amaçlı bir SignalR hub (Faz 1'in
  `/health`'i, Faz 3'ün `/whoami`'si gibi — altyapının çalıştığını
  kanıtlayan, spekülatif olmayan bir yapı taşı).

`packages/api-client/`:
- `package.json`, `tsconfig.json`, codegen script'i, generate edilen
  `.d.ts`/client dosyaları (gitignore'a mı girecek yoksa commit mi
  edilecek — plan aşamasında netleştirilecek, muhtemelen commit
  edilir ki `apps/web` build'i `apps/api`'nin ayakta olmasına bağımlı
  olmasın).

## Kapsam dışı

- `apps/web`'in bu client'ı gerçekten kullanması (henüz hiçbir sayfa
  API'ye ihtiyaç duymuyor; bu, ürün henüz belirlenmediği için
  spekülatif olurdu).
- SignalR üzerinden gerçek bir domain event yayını (outbox → SignalR
  köprüsü) — bu, gerçek bir dispatcher/consumer gerektirir, Faz 4'ün
  kapsam kararıyla aynı gerekçeyle erteleniyor.
- Mobile/admin app'lerinin client'ı tüketmesi (henüz boş app'ler).

## Kabul kriterleri

1. `PUT /{tenant-alias}/api/v1/tenants/{tenantId}` gerçek bir HTTP
   isteğiyle tenant'ı yeniden adlandırıyor; auth/tenant/membership/
   permission/tenant-scope zincirinin tamamı (Faz 3+4) HTTP üzerinden
   çalışıyor.
2. `Result` başarısızlıkları doğru HTTP status kodlarına eşleniyor
   (400/403/404).
3. `/openapi/v1.json` (veya benzeri) geçerli bir OpenAPI şeması
   döndürüyor, yeni endpoint'i içeriyor.
4. `packages/api-client`'ın build'i bu şemadan gerçek, derlenen
   TypeScript tipleri + bir client fonksiyonu üretiyor.
5. SignalR hub'ı bağlanabilir durumda (temel bir connect/disconnect
   testi geçiyor).
6. `pnpm --filter api build`, `pnpm --filter api test`, ve `pnpm
   --filter @st/api-client build` (veya paketin gerçek adı) başarılı.
