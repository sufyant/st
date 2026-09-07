# apps/api Phase 1: .NET solution bootstrap

Date: 2026-09-07
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

`apps/api` şu an tamamen boş (`.gitkeep` dışında hiçbir şey yok). Bu faz,
sonraki fazların (domain, persistence, auth, CQRS, API yüzeyi, test/
observability) üzerine inşa edeceği çalışan, derlenen, `pnpm dev` ile
ayağa kalkan bir .NET 10 solution iskeleti kurar. Bu fazda iş mantığı
yok — sadece "iskelet ayakta duruyor, tek komutla çalışıyor, testi
geçiyor" kanıtlanır.

Referans kararlar: [ADR 0028](../../adr/0028-dotnet-10-runtime.md) (.NET 10
LTS), [ADR 0029](../../adr/0029-monorepo-tooling.md) (Turborepo + ince
package.json wrapper).

## Proje yapısı

Diğer ADR'lerin dilinde (Domain/Application/Infrastructure ayrımı —
[ADR 0007](../../adr/0007-domain-modeling-building-blocks.md),
[ADR 0009](../../adr/0009-cqrs-lite.md),
[ADR 0010](../../adr/0010-hand-rolled-mediator-pipeline.md)) zaten ima
edilen standart katmanlı ayrım kullanılır — bu, ADR'lerde birebir
"Clean Architecture" olarak adlandırılmasa da, Domain'in Infrastructure'a
bağımlı olmaması ("iç katmanlar dış katmanları bilmez") gereksinimini
karşılayan en doğal proje bölünmesi:

```
apps/api/
  global.json                          # .NET 10 SDK'ya pinler
  Api.sln
  package.json                         # ince pnpm wrapper (ADR 0029)
  src/
    Api.Domain/
      Api.Domain.csproj
    Api.Application/
      Api.Application.csproj           # -> references Api.Domain
    Api.Infrastructure/
      Api.Infrastructure.csproj        # -> references Api.Application
    Api.Host/
      Api.Host.csproj                  # -> references Api.Infrastructure
      Program.cs
      appsettings.json
  tests/
    Api.Tests.Unit/
      Api.Tests.Unit.csproj            # -> references Api.Host
```

Bu fazda her katman projesi neredeyse boş kalır — sadece referans
zincirini kurmak ve derlenmesini sağlamak amaçlı. `Api.Host` bir
ASP.NET Core minimal API projesi olarak `dotnet run` ile ayağa kalkar ve
tek bir `GET /health` endpoint'i döner (`{"status":"ok"}`), böylece
"uçtan uca çalışıyor" iskelet düzeyinde kanıtlanmış olur. `Api.Tests.Unit`
tek bir trivial testle (`1+1==2` değil, gerçek ama minimal: `Api.Domain`
projesinin derlenip yüklenebildiğini doğrulayan bir smoke test) `dotnet
test`'in çalıştığını kanıtlar — gerçek domain/persistence testleri
sonraki fazların işi.

## Monorepo entegrasyonu

`apps/api/package.json` (gerçek bir Node.js paketi değil, ADR 0029'daki
ince wrapper):

```json
{
  "name": "api",
  "private": true,
  "scripts": {
    "dev": "dotnet watch --project src/Api.Host run",
    "build": "dotnet build Api.sln",
    "test": "dotnet test Api.sln"
  }
}
```

`turbo.json`'daki mevcut `dev`/`build`/`test` görevleri bu script'leri
otomatik olarak devreye alır — turbo.json'da ekstra bir değişiklik
gerekmiyor (paket bazlı script eşleşmesi zaten var olan task
tanımlarıyla çalışır). `check-types` ve `test:e2e` görevleri bu pakette
tanımlı değil, turbo bunları bu paket için otomatik atlar.

`global.json`, SDK'yı `10.0.102`'ye (mevcut ortamda kurulu sürüm) `latestFeature`
roll-forward politikasıyla pinler — böylece `9.0.302` da kurulu olsa
`dotnet` komutları her zaman .NET 10'u kullanır ([ADR 0028](../../adr/0028-dotnet-10-runtime.md)).

## Test stratejisi (bu faz için)

[ADR 0025](../../adr/0025-test-strategy.md) xUnit + Testcontainers'ı
zorunlu kılıyor, ama Testcontainers gerçek bir veritabanı bağlamı
gerektirir — bu faz henüz persistence katmanı kurmuyor. Bu yüzden bu
fazda sadece xUnit test projesinin iskeleti ve tek bir smoke test
kurulur; Testcontainers bağımlılığı ve gerçek entegrasyon testleri
Faz 2'de (persistence katmanıyla birlikte) eklenir. Bu, ADR 0025'i
ihlal etmiyor — "day 1'den itibaren test" ilkesi burada "test projesi
day 1'de var ve çalışıyor" olarak karşılanıyor; gerçek entegrasyon
testleri, test edilecek gerçek bir persistence katmanı ortaya çıkınca
(Faz 2) yazılıyor.

## Kapsam dışı

- Domain/Application/Infrastructure projelerinin içi (Faz 2+).
- CI pipeline kurulumu (bu ADR setinde karar verilmedi, kapsam dışı).
- Docker/deployment manifestleri (kapsam dışı, hiçbir ADR bunu talep
  etmiyor).

## Kabul kriterleri

1. `cd apps/api && dotnet build Api.sln` başarılı.
2. `cd apps/api && dotnet test Api.sln` başarılı, 1 test geçiyor.
3. `pnpm --filter api build` ve `pnpm --filter api test` kökten
   çalışıyor; kök `pnpm build`/`pnpm test` de Turborepo üzerinden aynı
   script'lere ulaşıyor.
4. `dotnet watch --project src/Api.Host run` ile host ayağa kalkıp
   `GET /health` çağrısına `{"status":"ok"}` döner (manuel/otomatik
   doğrulama — arka planda başlatıp curl ile kontrol edilecek, sonra
   kapatılacak).
