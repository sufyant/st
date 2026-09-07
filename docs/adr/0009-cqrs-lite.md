# 9. CQRS-lite: Ayrı Command/Query Sınıfları, Full CQRS Değil

**Status:** Accepted
**Date:** 2026-09-07

## Context

Yazma (command) ve okuma (query) operasyonlarının kod organizasyonu net
olmalı. Full CQRS (ayrı okuma/yazma veritabanları, event sourcing,
eventual consistency) endüstri literatüründe popüler ama önemli bir
operasyonel/kavramsal karmaşıklık maliyeti taşıyor.

## Decision

Command (yazma) ve Query (okuma) için ayrı, küçük class'lar +
handler'lar kullanılır (her biri tek bir iş yapan, tek sorumluluklu
sınıflar). Ancak hem command hem query aynı veritabanına, aynı EF Core
`DbContext`'e karşı çalışır — ayrı bir read-model veritabanı veya event
sourcing yok.

## Consequences

### Positive
- Her command/query kendi dosyasında, tek bir sorumlulukla yaşar —
  Ousterhout'un "derin modül" prensibiyle uyumlu: küçük bir arayüz
  (Handle metodu), net bir sorumluluk.
- Okuma tarafı, yazma tarafının domain kurallarıyla kısıtlanmadan,
  ihtiyaca göre optimize sorgular (gerekirse Dapper ile,
  [ADR 0012](0012-orm-ef-core-and-dapper.md)) yazabilir.

### Trade-offs
- Full CQRS'in sunduğu okuma/yazma ölçeklendirmesini bağımsızlaştırma
  avantajı yok — okuma yükü arttığında hâlâ aynı veritabanına gider.

## Alternatives Considered

### Full CQRS (ayrı read/write DB, event sourcing)
Yüksek ölçekte okuma/yazma yükünü ayrı ayrı ölçeklendirme ve tam denetim
kaydı (event sourcing) gibi gerçek avantajları var, ama bu projenin
bugünkü ihtiyacı bu değil — ürün henüz belirlenmemiş bir starter kit.
Ousterhout'un "A Philosophy of Software Design" kitabındaki merkezi
tezi şu: karmaşıklık, sistemi anlamayı ve değiştirmeyi zorlaştıran her
şeydir; event sourcing ve eventual consistency, bugün var olmayan bir
ölçek problemini çözmek için her geliştiricinin zihinsel modeline "bu
veri şu an tutarlı mı, yoksa henüz projeksiyon güncellenmedi mi"
sorusunu ekler. Bu maliyet, gerçek bir ihtiyaç ortaya çıkmadan
üstlenilmiyor.

### Generic CRUD servisleri (command/query ayrımı yok)
Daha az dosya, ama her servis zamanla hem okuma hem yazma sorumluluğunu
taşıyan şişkin sınıflara dönüşme eğilimindedir; command/query ayrımı bu
şişmeyi baştan engeller.
