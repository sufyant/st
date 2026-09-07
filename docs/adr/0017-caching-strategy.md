# 17. Caching: Genel Katman Yok (Henüz), Tenant-Resolution için IMemoryCache

**Status:** Accepted
**Date:** 2026-09-07

## Context

Genel bir caching stratejisi (örn. Redis, distributed cache) erken
kurulursa, hangi verinin ne kadar süre cache'leneceği, invalidation'ın
nasıl tetikleneceği gibi kararlar henüz ürün ihtiyaçları netleşmeden
verilmiş olur. Ancak tenant çözümleme
([ADR 0004](0004-path-based-tenant-resolution.md)) her istekte çalışan,
kesin bir "hot path" — her request `admin` schema'sına tenant lookup
sorgusu atmak gereksiz bir tekrar.

## Decision

Genel amaçlı bir cache katmanı şimdilik eklenmiyor. Tek istisna:
tenant-resolution lookup'ı (alias → tenant_id eşlemesi), her istekte
tekrarlanan, kesin bir hot path olduğu için `IMemoryCache` ile
cache'leniyor. Gelecekte query-level caching eklenebilmesi için bir
`ICachedQuery` işaretleyici (marker) arayüzü şimdiden tanımlanıyor, ama
caching davranışının kendisi (pipeline behavior olarak) henüz
yazılmıyor.

## Consequences

### Positive
- Ousterhout'un "tactical tornado" (ihtiyaç doğmadan spekülatif
  genellik eklemek) eleştirisinden kaçınılıyor: bugün gerçek bir
  performans ihtiyacı ölçülmeden distributed cache/invalidation
  stratejisi tasarlanmıyor.
- Tenant-resolution cache'i, gerçek ve ölçülebilir bir hot path'i (her
  istek) hedef aldığı için, spekülatif değil kanıtlanmış bir
  optimizasyon.
- `ICachedQuery` marker'ı, Pragmatic Programmer'ın "bugün karar verme
  ama kapıyı kapatma" ilkesiyle, gelecekte genel query caching
  eklenmek istendiğinde hangi query'lerin cache'e aday olduğunu
  işaretlemek için hazır bir yer bırakıyor — ama davranış yazılana
  kadar bu marker hiçbir çalışma zamanı etkisi yaratmıyor.

### Trade-offs
- `IMemoryCache`, tek bir process'in belleğinde yaşar; API birden fazla
  instance'da çalıştığında (yatay ölçekleme) her instance kendi
  tenant-resolution cache'ini ayrı ayrı tutar — bu, tenant-resolution
  verisinin nadiren değiştiği (yeni tenant oluşturma nadir bir olay)
  göz önüne alınarak kabul edilebilir bir trade-off.

## Alternatives Considered

### Baştan Redis/distributed cache kurmak
Yatay ölçeklemede tutarlı bir cache sağlardı, ama bugün var olmayan bir
ihtiyaç için ek bir altyapı bileşeni (Redis instance, bağlantı
yönetimi, invalidation stratejisi) kurmak, projenin bugünkü ölçeğinde
sadece operasyonel yük ekler. Gerçek bir ihtiyaç (örn. ölçülmüş query
yavaşlığı) ortaya çıktığında `ICachedQuery` marker'ı zaten hazır olduğu
için, o noktada distributed cache'e geçiş mevcut mimariyi bozmadan
yapılabilir.
