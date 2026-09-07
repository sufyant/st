# 3. Auth/Authorization Ayrımı: Clerk Sadece Kimlik Doğrulama İçin

**Status:** Accepted
**Date:** 2026-09-07

## Context

Clerk, kimlik doğrulama (kullanıcı kimdir) ve "Organizations" adında
kendi multi-tenancy/authorization modelini sunuyor. Bu projede tenant
modeli zaten kendi `admin` schema'sında tanımlı
([ADR 0001](0001-tenant-isolation-schema-per-tenant.md),
[ADR 0002](0002-control-plane-admin-schema.md)) ve path-based tenant
çözümleme ([ADR 0004](0004-path-based-tenant-resolution.md)) kullanılıyor
— Clerk Organizations'ın subdomain/organization-id merkezli modeliyle
örtüşmüyor.

## Decision

Clerk yalnızca authentication (kullanıcının kimliği, oturumu, JWT
üretimi) için kullanılır; Clerk Organizations kullanılmaz. Tenant, rol
ve membership tamamen `admin` schema'sında yönetilir. .NET backend,
Clerk'in resmi .NET SDK'sını kullanmadan, standart ASP.NET Core JWT
Bearer authentication + Clerk'in JWKS endpoint'i üzerinden token
doğrulaması yapar.

## Consequences

### Positive
- Tenant/rol modeli tamamen bu projenin kontrolünde; Clerk
  Organizations'ın veri modeline (org rolleri, org metadata limitleri)
  bağımlı kalınmaz.
- JWT Bearer + JWKS, ASP.NET Core'un yerleşik
  `Microsoft.AspNetCore.Authentication.JwtBearer` middleware'iyle native
  olarak desteklenir — üçüncü parti SDK bağımlılığı yok, `Authority`/
  `Audience` standart konfigürasyon.
- Clerk hesap sağlayıcısı değiştirilmek istenirse (örn. Auth0'a geçiş),
  sadece JWT doğrulama katmanı değişir; tenant/rol modeli etkilenmez.

### Trade-offs
- Clerk Organizations'ın hazır sunduğu davet/üyelik UI bileşenlerinden
  faydalanılamaz; davet akışı ([ADR 0005](0005-membership-and-invitation.md))
  elle inşa edilmeli.
- Clerk'in resmi SDK'sının sağladığı ek kolaylıklar (örn. tip-güvenli
  backend API client'ı) kullanılmıyor; JWKS doğrulaması ve gerekirse
  Clerk Backend API çağrıları elle entegre edilir.

## Alternatives Considered

### Clerk Organizations kullanmak
Kimlik doğrulama ile tenant/authorization modelini tek sağlayıcıya
bağlamak hızlı bir başlangıç sağlardı, ama iki farklı endişeyi (kullanıcı
kimdir / kullanıcı hangi tenant'ta ne yapabilir) tek bir üçüncü parti
modele kilitlemiş olurdu. Bu projede zaten path-based tenant çözümleme
ve kendi permission sistemi ([ADR 0006](0006-permission-system.md))
planlanıyor; Clerk Organizations'ın kendi org-rol modeliyle bunları
senkronize tutmak, tek bir kaynaktan yönetmekten daha fazla cognitive
load yaratırdı.

### Clerk'in resmi .NET SDK'sını kullanmak
SDK, Clerk'e özel kolaylıklar sunar ama backend'i Clerk'in SDK sürüm
döngüsüne bağımlı kılar. Standart JWT Bearer + JWKS, OpenID Connect'in
parçası olan evrensel bir mekanizma; herhangi bir OIDC-uyumlu
sağlayıcıyla (Clerk dahil) aynı şekilde çalışır ve ASP.NET Core'un kendi
authentication pipeline'ına ekstra soyutlama katmanı eklemeden entegre
olur.
