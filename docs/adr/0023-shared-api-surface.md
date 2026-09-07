# 23. API Yüzeyi: Başlangıçta Tek Ortak Yüzey, BFF Sonra

**Status:** Accepted
**Date:** 2026-09-07

## Context

Bazı referans mimarilerde mobil istemciler için ayrı bir
Backend-for-Frontend (BFF) katmanı kurulur (mobil'e özel, optimize
edilmiş endpoint'ler). Bu projede henüz web/mobile/admin arasında UX'in
gerçekten ayrışacağı netleşmedi.

## Decision

web/mobile/admin başlangıçta aynı, tek ortak endpoint yüzeyini paylaşır.
Mobil-specific BFF ayrımı, sadece UX gerçekten ayrışırsa (örn. mobil
için ayrı, agregat edilmiş bir "dashboard" endpoint'i gerçek bir
performans/kullanılabilirlik problemini çözüyorsa) eklenir.

## Consequences

### Positive
- Bugün var olmayan bir UX ayrışması için ayrı bir BFF katmanı (ek
  deployment birimi, ek bakım yüzeyi) baştan kurulmuyor — YAGNI.
- Tek yüzey, `packages/api-client`'ın
  ([ADR 0022](0022-generated-typescript-client.md)) tüm istemciler için
  tek, tutarlı bir kaynak olmasını basitleştiriyor.

### Trade-offs
- Mobil'e özgü bir performans ihtiyacı (örn. tek ekranın birden fazla
  ayrı API çağrısı gerektirmesi) ortaya çıkarsa, bu ihtiyaç BFF
  eklenene kadar istemci tarafında birden fazla çağrıyı birleştirerek
  (client-side aggregation) veya backend'de yeni bir agregat endpoint
  ekleyerek geçici olarak çözülebilir.

## Alternatives Considered

### Baştan mobil-specific BFF kurmak
Referans şirketin yaptığı gibi baştan ayrı bir BFF katmanı kurmak,
bugün henüz kanıtlanmamış bir UX ayrışması varsayımına dayanır.
Pragmatic Programmer'ın "spekülatif genellik ekleme" uyarısıyla uyumlu
olarak, bu ayrım gerçek bir ihtiyaç ortaya çıktığında (ölçülmüş bir
performans problemi veya gerçekten farklı bir mobil UX) eklenmek üzere
erteleniyor.
