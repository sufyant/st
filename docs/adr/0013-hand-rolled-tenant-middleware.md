# 13. Tenant Middleware: Hazır Kütüphane Değil, Elle Yazıldı

**Status:** Accepted
**Date:** 2026-09-07

## Context

.NET ekosisteminde Finbuckle.MultiTenant gibi hazır multi-tenancy
kütüphaneleri var; tenant çözümleme
([ADR 0004](0004-path-based-tenant-resolution.md)), schema seçimi
([ADR 0001](0001-tenant-isolation-schema-per-tenant.md)) ve middleware
sıralaması ([ADR 0006](0006-permission-system.md)) gibi işleri hazır bir
soyutlama arkasında sunuyorlar.

## Decision

Tenant çözümleme middleware'i elle yazılır; Finbuckle.MultiTenant veya
benzeri bir kütüphane kullanılmaz.

## Consequences

### Positive
- Bu, sistemin en güvenlik-kritik katmanı: bir tenant'ın verisine
  yanlışlıkla başka bir tenant'ın erişmesi ihtimali burada kapanıyor.
  Elle yazılmış bir middleware, davranışının her satırı bu projenin
  geliştiricileri tarafından okunmuş, anlaşılmış ve test edilmiş
  (ayrı `TenantIsolationTests` paketi,
  [ADR 0025](0025-test-strategy.md)) olur.
- Hazır kütüphanenin genel amaçlı soyutlamasının (birden fazla tenant
  çözümleme stratejisini desteklemek, birden fazla store tipini
  desteklemek gibi) bu projede hiç kullanılmayacak kısımları hiç var
  olmuyor — Ousterhout'un vurguladığı "genel amaçlı kod, kullanılmayan
  esnekliğin cognitive load'unu her geliştiriciye taşır" maliyetinden
  kaçınılıyor.
- Kütüphanenin kendi güncelleme döngüsüne, breaking change'lerine veya
  terkedilme riskine bağımlı kalınmıyor.

### Trade-offs
- Finbuckle gibi bir kütüphanenin topluluk tarafından zaten keşfedilmiş
  edge-case'lerini (örn. concurrent request'lerde tenant context'in
  doğru izole edilmesi) bu proje kendi başına keşfetmeli ve test
  etmeli.

## Alternatives Considered

### Finbuckle.MultiTenant
Olgun, aktif geliştirilen bir kütüphane — ama genel amaçlı olduğu için
bu projenin path-based + schema-per-tenant + admin-schema-membership
kombinasyonuna özgü olmayan bir API yüzeyi sunuyor. Güvenlik kritik bir
katmanda "kütüphanenin nasıl çalıştığını tam olarak anlama" maliyeti,
"bu katmanı kendimiz yazıp tam kontrolü elimizde tutma" maliyetinden
daha yüksek görüldü.
