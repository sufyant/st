# 19. Temporal: Şimdilik Eklenmedi

**Status:** Accepted
**Date:** 2026-09-07

## Context

Temporal, çok-adımlı, uzun süreli, dayanıklı (durable) iş akışlarını
(örn. "ödeme al → 3 gün bekle → hatırlatma gönder → 7 gün sonra iptal
et") güvenilir şekilde modellemek için kullanılan bir workflow
orchestration motoru. Bu proje henüz böyle bir gerçek ihtiyaç ortaya
koymuyor — outbox pattern ([ADR 0018](0018-outbox-pattern.md))
tek-adımlı, transaction-bazlı yan etkileri zaten çözüyor.

## Decision

Temporal (veya benzeri bir workflow motoru) şimdilik projeye
eklenmiyor. Gerçek bir çok-adımlı/dayanıklı workflow ihtiyacı ortaya
çıkarsa, self-hosted (ücretsiz) Temporal, mevcut mimariyi bozmadan
eklenecek şekilde değerlendirilecek.

## Consequences

### Positive
- Bugün var olmayan bir ihtiyaç için yeni bir altyapı bileşeni
  (Temporal server, worker deployment, yeni bir programlama modeli
  öğrenme maliyeti) üstlenilmiyor. Ousterhout'un ifadesiyle, "gelecekte
  belki gerekecek" bir esneklik, bugünden her geliştiricinin
  öğrenmesi/bakması gereken bir karmaşıklık olarak var olmuyor.

### Trade-offs
- Gerçek bir çok-adımlı workflow ihtiyacı ortaya çıkarsa (örn. karmaşık
  bir onboarding süreci), bu ihtiyaç önce outbox + BackgroundService
  ([ADR 0018](0018-outbox-pattern.md)) gibi daha basit mekanizmalarla
  zorlanmaya çalışılabilir; bu mekanizmalar yetersiz kalırsa Temporal'ın
  eklenmesi ayrı bir mimari karar/ADR gerektirecek.

## Alternatives Considered

### Baştan Temporal kurmak
The Pragmatic Programmer'ın "gereksinim netleşmeden mimariyi kilitleme"
ilkesiyle çelişir — Temporal'ın çözdüğü problem (dayanıklı, çok-adımlı
workflow) bu projede henüz var olmayan bir problem; erken eklemek hem
öğrenme maliyeti hem operasyonel yük (self-hosted çalıştırmak için ayrı
bir servis) getirir, karşılığında bugün hiçbir kullanım senaryosu yok.
