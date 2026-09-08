# apps/api Phase 6: Test + Observability Infra

Date: 2026-09-08
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

`apps/api`'nin son build fazı: [ADR 0026](../../adr/0026-structured-logging.md)'nın
öngördüğü Serilog + Seq yapılandırılmış loglamasını ekler, ve
[ADR 0025](../../adr/0025-test-strategy.md)'in gün-1'den beri erteliyor
olduğu ayrı, isimli `TenantIsolationTests` paketini oluşturur. xUnit +
Testcontainers zaten Faz 2b'den beri kullanılıyor ("formalization" burada
yeni bir altyapı kurmak değil, bu ikisini tamamlamak anlamına geliyor).

## Kapsam kararı 1: Serilog enrichment, ADR 0026'nın belirttiği yerde

ADR 0026 enrichment'ın "merkezi pipeline'da (ADR 0010'daki pipeline
behavior zincirinin bir parçası olarak)" olmasını açıkça istiyor. Bu,
Faz 4'ün `LoggingBehavior`'ını (şu an generic `ILogger<T>` kullanan)
`Serilog.Context.LogContext.PushProperty` ile tenant_id/request_id/
user_id enrich edecek şekilde genişletmek anlamına geliyor. Bunu yapmak
için `IHttpContextAccessor`'a ihtiyaç var (tenant_id/user_id claim'leri
oradan okunuyor) — ama `Api.Application` bilinçli olarak ASP.NET Core
soyutlamalarından bağımsız tutuluyor (Faz 3'ün mimari kararı); tam bu
yüzden Faz 4'ün `PermissionBehavior`'ı da `IHttpContextAccessor`
kullandığı için `Api.Application`'da değil `Api.Infrastructure`'da yaşıyor.
Bu fazda `LoggingBehavior`, aynı gerekçeyle `Api.Application`'dan
`Api.Infrastructure`'a taşınıyor — `PermissionBehavior`/
`SaveChangesUnitOfWorkBehavior` ile aynı yere, HttpContext'e bağımlı
pipeline behavior'ların toplandığı katmana. `Program.cs`, DI kaydını
(`Api.Application.LoggingBehavior<,>` yerine
`Api.Infrastructure.LoggingBehavior<,>`) buna göre günceller; kayıt sırası
(Logging en dışta) değişmiyor. `Program.cs` ayrıca `builder.Host.UseSerilog(...)`
ile varsayılan `Microsoft.Extensions.Logging`'in yerine Serilog'u koyar
(Console sink + Seq sink, Seq URL `appsettings.json`'dan `Seq:ServerUrl`,
varsayılan `http://localhost:5341`).

**Not:** Bu enrichment sadece mediator üzerinden geçen istekleri kapsar
(şu an sadece `RenameTenantCommand`) — `/health`/`/whoami` gibi mediator
kullanmayan endpoint'ler bu fazda enrich edilmiyor. Bu, ADR 0026'nın
metninin birebir izlenmesinin bir sonucu; ADR'nin kendisi "merkezi
pipeline" derken spesifik olarak ADR 0010'un pipeline'ını referans alıyor.

## Kapsam kararı 2: `TenantIsolationTests` — dağınık değil, kendi başına eksiksiz

ADR 0025'in gerekçesi açık: "tenant izolasyonu test edildi mi" sorusunun
cevabı, kod tabanında dağınık olmak yerine tek, bilinen bir yerde
toplanmalı. Bugün bu garantiler zaten Faz 3/4/5'in kendi test dosyalarında
(scattered) test ediliyor —
`TenantResolutionMiddlewareTests`, `AuthPipelineEndToEndTests`,
`RenameTenantCommandEndToEndTests`'in cross-tenant testi,
`RenameTenantEndpointTests`'in 403 testi gibi. Bu faz onları taşımıyor
(mevcut testler kendi dosyalarında kalıyor, hiçbiri silinmiyor) — yeni bir
`Api.Tests.TenantIsolation` projesi, sistemin tenant izolasyonu
garantilerinin *tamamını* kendi başına, tek bir yerden okunarak
doğrulayan, taze yazılmış bir test seti içeriyor. Bu, mevcut testlerle
mantıksal olarak örtüşecek (örn. cross-tenant write reddi hem burada hem
`RenameTenantCommandEndToEndTests`'te test edilecek) — bu ADR'nin kendi
gerekçesinin doğrudan sonucu, kaçınılması gereken bir "gereksiz
duplikasyon" değil.

## Eklenecek dosyalar (üst düzey)

`Api.Host/`:
- `appsettings.json` (veya `appsettings.Development.json`): `Seq:ServerUrl`.
- `Program.cs`: `builder.Host.UseSerilog(...)`.

`Api.Infrastructure/`:
- `LoggingBehavior.cs` (Api.Application'dan taşınıyor, genişletilecek): `Serilog.Context.LogContext.PushProperty` ile tenant_id/request_id/user_id. `Api.Application/LoggingBehavior.cs` bu fazda silinir.

`Api.Tests.TenantIsolation/` (yeni proje, `apps/api/tests/` altında, mevcut `Api.Tests.Unit`/`Api.Tests.Integration` ile aynı Testcontainers + xUnit paternini izler):
- `SchemaIsolationTests.cs` — iki tenant provision edildiğinde, gerçekten iki ayrı Postgres schema'sı oluştuğunu (`information_schema.schemata` sorgusuyla) doğrular.
- `CrossTenantMembershipTests.cs` — tenant A'ya üye bir kullanıcı, tenant B'nin alias'ına istek attığında `TenantResolutionMiddleware`'in membership kontrolünden 403 aldığını doğrular.
- `CrossTenantWriteTests.cs` — tenant A için authenticate olmuş (ve "tenant.rename" iznine sahip) bir kullanıcının, `RenameTenantCommand`'ı tenant B'nin id'siyle çağırmaya çalıştığında `ITenantScopedRequest` kontrolünün reddettiğini, HTTP üzerinden (`PUT /{B-alias}/api/v1/tenant` — ama caller'ın token'ı A için) doğrular.
- `JwtClaimInjectionTests.cs` — JWT'nin kendisinde sahte `tenant_id`/`permission` claim'leri taşıyan bir token, gerçek tenant çözümlemesinden gelen claim'leri override edemediğini doğrular (Faz 3'ün stale-claim-removal fix'inin, kendi ayrı, isimlendirilmiş "tenant isolation" evi içinde de kanıtlanması).

## Kapsam dışı

- Gerçek zamanlı log alerting/dashboard kurulumu — Seq'in kendisi zaten bir arayüz sağlıyor, üstüne ek bir şey kurulmuyor.
- Grafana/Loki'ye geçiş — ADR 0026'da gelecekteki bir seçenek olarak not edilmiş, bu fazın kapsamı değil.
- Mediator kullanmayan endpoint'lerin (`/health`, `/whoami`) log enrichment'ı — ADR 0026'nın metninin birebir kapsamı dışında (yukarıdaki not).
- `TenantDbContext`'in gerçek entity'lerle doldurulması — hâlâ boş, gerçek ürün henüz belirlenmedi, bu fazın işi değil.

## Kabul kriterleri

1. `Program.cs` Serilog ile başlıyor, Console + Seq sink'lerine yazıyor.
2. `LoggingBehavior`, mediator üzerinden geçen her istekte log context'e tenant_id/request_id/user_id ekliyor — bu, gerçek bir log çıktısı yakalanarak (test sırasında) doğrulanıyor, sadece kodun "yapması gerekeni" varsayılarak değil.
3. `Api.Tests.TenantIsolation` projesi var, xUnit + Testcontainers kullanıyor, yukarıdaki 4 test dosyasının tamamını içeriyor, hepsi gerçek Postgres'e karşı geçiyor.
4. `pnpm --filter api build` ve `pnpm --filter api test` (yeni `Api.Tests.TenantIsolation` projesi dahil, solution'a eklenmiş) başarılı.
