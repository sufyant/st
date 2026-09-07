# 21. API Stili: REST, `/api/v1`, GraphQL/tRPC Değil

**Status:** Accepted
**Date:** 2026-09-07

## Context

`apps/api`'nin web/mobile/admin istemcilerine sunacağı API'nin stiline
(REST/GraphQL/tRPC) ve versiyonlama disiplinine karar verilmeli.

## Decision

REST kullanılır, tüm endpoint'ler `/api/v1` altında yaşar. Versiyonlama
additive-only disiplinine tabidir: mevcut bir endpoint'e yeni, opsiyonel
alan eklemek serbesttir, ama breaking change (bir alanı kaldırmak,
tipini değiştirmek, zorunlu hale getirmek) her zaman yeni bir versiyona
(`/api/v2`) gider. Tenant çözümlemesiyle
([ADR 0004](0004-path-based-tenant-resolution.md)) birleştiğinde
kanonik URL şekli `/{tenant-alias}/api/v1/...` olur; davet kabulü gibi
kullanıcının henüz bir membership'i olmadığı birkaç endpoint bu tenant
prefix'inin dışında yaşamak zorundadır
([ADR 0005](0005-membership-and-invitation.md)'deki davet kabul akışı).

## Consequences

### Positive
- REST, ASP.NET Core'un native desteklediği, OpenAPI şema üretimi
  ([ADR 0022](0022-generated-typescript-client.md)) ile doğrudan uyumlu
  bir stil — ek bir GraphQL sunucusu/resolver katmanı veya tRPC'nin
  TypeScript-merkezli varsayımlarını .NET tarafına taşıma ihtiyacı yok.
- Additive-only disiplini, mevcut istemcilerin (web/mobile/admin farklı
  sürümlerde dağıtılmış olabilir) hiçbir uyarı olmadan kırılmasını
  engeller — yeni bir mobil app sürümü store onayı beklerken, backend'de
  yapılan bir değişiklik eski app sürümünü bozmaz.

### Trade-offs
- GraphQL'in sunduğu "istemci sadece ihtiyacı olan alanları ister"
  esnekliği yok; over-fetching/under-fetching riski REST'in doğal bir
  sınırlaması olarak kabul ediliyor.
- Breaking change'ler yeni versiyona gittiği için, eski versiyonların ne
  zaman kaldırılacağına dair bir deprecation politikası zamanla
  gerekecek (bu ADR'nin kapsamı dışında, ihtiyaç doğduğunda ayrı bir
  karar).

## Alternatives Considered

### GraphQL
Esnek sorgulama sağlar ama .NET tarafında resolver/schema katmanı ek
bir öğrenme ve bakım yüzeyi ekler; bu projenin öngörülebilir, sabit
şekilli endpoint ihtiyaçları (CRUD + birkaç özel işlem) için bu
esnekliğin getirdiği karmaşıklık karşılığını vermiyor.

### tRPC
TypeScript uçtan uca tip güvenliği sağlar ama .NET backend ile doğal
uyumlu değil (tRPC temelde TypeScript-to-TypeScript bir araç); bu proje
zaten OpenAPI'den TypeScript client üretimiyle
([ADR 0022](0022-generated-typescript-client.md)) benzer bir uçtan uca
tip güvenliğini .NET-uyumlu bir yoldan elde ediyor.
