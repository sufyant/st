# apps/api Phase 3: Auth + Tenant Resolution + Permission Middleware

Date: 2026-09-08
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

Bu faz, sistemin en güvenlik-kritik katmanını kurar: kimlik doğrulama
([ADR 0003](../../adr/0003-auth-authorization-split.md)), path-based
tenant çözümleme + membership kontrolü
([ADR 0004](../../adr/0004-path-based-tenant-resolution.md),
[ADR 0013](../../adr/0013-hand-rolled-tenant-middleware.md)), ve
permission kontrolü
([ADR 0006](../../adr/0006-permission-system.md)). Phase 2b'nin
kurduğu `AdminDbContext`/`Tenant`/`User`/`Membership`/`RolePermission`
üzerine inşa edilir.

## Sıralama netliği: ADR 0006'nın "pipeline behavior" cümlesi

ADR 0006, permission kontrolünün "pipeline behavior'da" yapıldığını
söylüyor, ama mediator/pipeline (ADR 0010) henüz kurulmadı — o Faz
4'ün işi. Bu fazda permission kontrolü ASP.NET Core'un native
claims/policy-based authorization mekanizmasıyla, endpoint seviyesinde
(`[Authorize(Policy = "...")]` + özel bir `IAuthorizationHandler`)
uygulanır — ADR 0006'nın "ASP.NET Core native claims/policy-based
authorization" ifadesiyle birebir uyumlu. Faz 4 mediator'ü kurunca,
command/query handler'lar için ek bir `[RequiresPermission]` pipeline
behavior'ı bu native mekanizmanın üzerine eklenecek; bu, mevcut
policy altyapısını değiştirmez, sadece HTTP-dışı (CQRS handler)
çağrı yoluna da aynı kontrolü taşır.

## Kapsam kararı: sahte iş endpoint'i yok, tanı endpoint'i var

Faz 2b'deki "sahte iş varlığı uydurma" disiplini burada da geçerli:
gerçek bir korumalı iş endpoint'i (API yüzeyi Faz 5'in işi) henüz yok.
Bütün pipeline'ı (auth → tenant çözümleme → membership → permission)
uçtan uca kanıtlamak için `/{tenant}/api/v1/whoami` adında minimal,
tanı amaçlı bir endpoint eklenir (Faz 1'in `/health`'i gibi —
spekülatif bir ürün özelliği değil, altyapının çalıştığını kanıtlayan
bir araç). Sadece authenticated + tenant üyesi + belirli bir izne
sahip kullanıcıya `{tenantId, userId, role}` döner.

## JWT/JWKS test stratejisi

Gerçek bir Clerk instance'ına test sırasında ağ çağrısı yapılmaz.
`JwtBearerOptions.Authority` production'da Clerk'in JWKS endpoint'ine
işaret eder; test host'unda (`WebApplicationFactory` üzerinden)
`TokenValidationParameters.IssuerSigningKey` yerel, testte üretilen bir
RSA anahtarıyla override edilir — gerçek `JwtBearerHandler` doğrulama
mantığı (imza, süre, issuer) test edilir, sadece anahtar kaynağı
değişir. Bu, JWT bearer auth test etmenin standart, ağ bağımsız
yöntemi.

## Eklenecek dosyalar (üst düzey)

`apps/api/src/Api.Infrastructure/`:
- `TenantResolutionMiddleware.cs` — path'ten alias okur, `Tenant`
  bulur, authenticated user'ın `Membership`'ini kontrol eder,
  `ClaimsPrincipal`'a `tenant_id`/`tenant_slug`/`membership_role`
  claim'leri ekler.
- `PermissionAuthorizationHandler.cs` + `PermissionRequirement.cs` —
  `RolePermission` tablosuna göre, kullanıcının `membership_role`
  claim'inin istenen permission'a sahip olup olmadığını kontrol eder.

`apps/api/src/Api.Host/`:
- `Program.cs` güncellenir: JWT Bearer authentication, authorization
  policy'leri, `TenantResolutionMiddleware`, `/{tenant}/api/v1/whoami`
  endpoint'i.
- `appsettings.json`: `Clerk:Authority` placeholder.

`apps/api/tests/Api.Tests.Integration/`:
- `TestJwtTokenFactory.cs` — test JWT'leri üreten yardımcı sınıf.
- `CustomWebApplicationFactory.cs` — test host'unu yerel imzalama
  anahtarıyla yapılandıran `WebApplicationFactory<Program>` alt
  sınıfı.
- `TenantResolutionMiddlewareTests.cs`, `PermissionAuthorizationTests.cs`,
  `WhoAmIEndpointTests.cs` — gerçek Postgres + gerçek HTTP pipeline
  üzerinden uçtan uca testler.

## İstek işleme sırası (ADR 0006'daki sıralamanın bu fazda karşılığı)

1. `UseAuthentication()` — JWT doğrulanır, `ClaimsPrincipal.Identity`
   set edilir (henüz tenant/rol yok).
2. `TenantResolutionMiddleware` — path'ten alias, `Tenant` lookup,
   `Membership` lookup, claim ekleme. Tenant yoksa 404; kullanıcı
   authenticated değilse bu middleware çalışmaz (endpoint'in
   `[Authorize]` attribute'u zaten 401 döner); authenticated ama
   membership yoksa 403.
3. `UseAuthorization()` + endpoint'in `[Authorize(Policy = "...")]`'ı
   — `PermissionAuthorizationHandler` çalışır, yetkisizse 403.
4. Handler (`/whoami` bu fazda tek örnek).

## Kapsam dışı

- Gerçek Clerk instance'ına bağlanma/entegrasyon testi (bu makinede
  Clerk credential'ı yok; JWT doğrulama mantığı yerel anahtarla test
  ediliyor, gerçek Clerk JWKS endpoint'i sadece `Authority`
  konfigürasyonu olarak var, çağrılmıyor).
- `[RequiresPermission]` pipeline behavior'ı (Faz 4, mediator'e
  bağımlı).
- Workspace switcher, davet kabul akışı, gerçek kullanıcı/tenant
  onboarding UI'ı (bu API-only bir faz, frontend kapsam dışı).
- `search_path` bazlı runtime tenant-schema veri erişimi — Faz 2b'nin
  final review'ı bunu bilinçli olarak erteledi (gerçek bir tenant-schema
  iş varlığı/endpoint'i olmadan bu mekanizmayı kurmak spekülatif
  olurdu); bu fazın `TenantResolutionMiddleware`'i tenant'ı ve
  membership'i doğrular ama henüz hiçbir tenant-schema sorgusu
  çalıştırmıyor.

## Kabul kriterleri

1. Geçerli bir JWT olmadan `/{tenant}/api/v1/whoami`'ye istek 401
   döner.
2. Geçerli JWT ama `Membership`i olmayan bir tenant'a istek 403 döner.
3. Var olmayan bir tenant alias'ına istek 404 döner.
4. Geçerli JWT + `Membership` var ama gerekli permission'a sahip
   olmayan role → 403.
5. Geçerli JWT + `Membership` + gerekli permission'a sahip role → 200,
   body `{tenantId, userId, role}` içeriyor.
6. `pnpm --filter api build` ve `pnpm --filter api test` başarılı.
