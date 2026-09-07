# 27. Kod Standardı: Gereksiz Comment Yok, DDD Vocabulary, ADR Pratiği

**Status:** Accepted
**Date:** 2026-09-07

## Context

Kod tabanının okunabilirliği ve bakımı, tek tip bir yazım disiplinine
bağlı. Robert C. Martin'in Clean Code kitabı, iyi isimlendirilmiş,
küçük, tek-sorumluluklu kodun çoğu durumda yorum satırına ihtiyaç
duymadığını savunur — bir yorum genellikle kodun kendisinin yeterince
açık olmadığının bir işaretidir.

## Decision

Kod tabanında gereksiz comment yazılmaz (tek istisna: test'lerdeki AAA
etiketleri, [ADR 0025](0025-test-strategy.md)). Domain katmanında
ubiquitous language/DDD vocabulary (Entity, AggregateRoot, ValueObject,
DomainEvent —
[ADR 0007](0007-domain-modeling-building-blocks.md)) tutarlı şekilde
kullanılır. Mimari kararlar, bu dokümanın kendisinin de bir örneği
olduğu ADR pratiğiyle kayıt altına alınır.

## Consequences

### Positive
- Clean Code'un merkezi tezi: "kod neden bunu yapıyor" sorusunun cevabı
  bir yorumda değil, iyi isimlendirilmiş bir fonksiyon/değişken/sınıf
  isminde yaşamalı — bu, kodun kendisiyle senkron kalmayan (kod değişir
  ama yorum güncellenmeyi unutulur) bir belge riskini ortadan kaldırır.
- Mimari kararların "neden" kısmı (context, elenen alternatifler) kod
  yorumlarında değil, ayrı ADR dosyalarında yaşar — kod okunurken "ne"
  ve "nasıl" görünür, "neden" isteyen biri ADR'ye bakar; bu ayrım, kodun
  kendisini gereksiz tarihsel/gerekçesel metinle şişirmeden tutar.

### Trade-offs
- "Gereksiz" ile "gerekli" yorum arasındaki sınır bir miktar yargıya
  dayanır (örn. gizli bir kısıtlama, bir workaround'un nedeni gibi
  gerçekten non-obvious bir bilgi taşıyan yorumlar hâlâ yazılabilir) —
  bu disiplin, mekanik bir "hiç yorum yazma" kuralı değil, "yorumun kod
  ile açıklanamayan bir şey taşıyıp taşımadığını sorgula" alışkanlığı
  olarak uygulanır.

## Alternatives Considered

### Serbest, kişisel tercihe bağlı yorum yazımı
Her geliştirici kendi tercihine göre yorum yazarsa, kod tabanı zamanla
"kodun ne yaptığını tekrar eden" (WHAT) yorumlarla dolar — bu yorumlar
kod değiştikçe güncellenmeyi unutulur ve yanlış bilgi taşımaya başlar;
Clean Code'un uyardığı tam olarak bu risktir.
