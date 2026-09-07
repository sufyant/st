# 4. Tenant Çözümleme: Path Bazlı, Subdomain/Header Değil

**Status:** Accepted
**Date:** 2026-09-07

## Context

Bir gelen isteğin hangi tenant'a ait olduğu belirlenmeli. Yaygın üç
yöntem: subdomain (`acme.app.com`), custom header (`X-Tenant-Id`), veya
path prefix (`/acme/api/...`). Bu proje mobil uygulamayı (Expo/React
Native) day-1'den itibaren birinci sınıf istemci olarak planlıyor.

## Decision

Tenant, URL path'inin bir parçası olan `{tenant-alias}` segmentiyle
çözümlenir: `/{tenant-alias}/api/...`. Merkezi bir ASP.NET Core
middleware bu alias'ı okur, `admin` schema'sında tenant'ı ve kullanıcının
membership'ini doğrular, sonucu `ClaimsPrincipal`'a tenant_id/role/
permission claim'leri olarak ekler.

## Consequences

### Positive
- Mobil istemciler (Expo/React Native) için subdomain bazlı çözümleme
  pratikte zordur (mobil networking DNS/subdomain bazlı çoklu-kiracılığı
  web kadar doğal desteklemez); path bazlı çözümleme tüm istemcilerde
  (web/mobile/admin) aynı şekilde çalışır.
- Tek bir domain/sertifika yeterli; wildcard subdomain sertifikası veya
  DNS yönetimi gerekmiyor.
- Tenant, URL'de görünür olduğu için debug/log okuması kolaylaşır (bir
  log satırındaki path'ten hangi tenant olduğu direkt okunur).

### Trade-offs
- Header bazlı yaklaşıma göre URL'ler biraz daha uzun ve tenant alias'ı
  her route tanımında bir prefix parametresi olarak taşınmalı.
- Path'teki tenant alias'ı ile authenticated kullanıcının gerçek
  membership'i her istekte karşılaştırılmalı — bu kontrolü atlayan bir
  route eklenirse güvenlik açığı oluşur; bu yüzden çözümleme merkezi
  middleware'de, route bazlı değil, tüm pipeline'ın en başında yapılır
  ([ADR 0006](0006-permission-system.md)'daki sıralama).

## Alternatives Considered

### Subdomain bazlı (`acme.app.com`)
Web için doğal bir çözüm ama mobil istemcilerde native olarak
desteklenmiyor (mobil app'in "hangi subdomain" olduğunu kullanıcı
girmesi/deep-link ile taşınması gerekirdi) — mobile-first bir starter kit
için ek karmaşıklık.

### Header bazlı (`X-Tenant-Id`)
Path'ten daha "temiz" URL'ler sağlar, ama header'lar log/debug/tarayıcı
adres çubuğunda görünmez, bu da manuel test ve destek sürecini
zorlaştırır. Ayrıca frontend'de her API çağrısına header enjekte eden
bir interceptor katmanı gerektirir; path bazlı yaklaşımda bu, route
yapısının doğal bir parçası.
