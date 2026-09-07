# 7. Domain Modelleme: Entity/AggregateRoot/ValueObject/DomainEvent

**Status:** Accepted
**Date:** 2026-09-07

## Context

Domain katmanının nasıl modelleneceğine dair ortak bir vocabulary ve
base class seti gerekiyor; aksi halde her geliştirici "entity" ve "value
object" kavramlarını farklı yorumlar, primitive obsession (her yeri
string/Guid ile modellemek) yaygınlaşır.

## Decision

Eric Evans'ın Domain-Driven Design kitabındaki terminoloji birebir
kullanılır: `Entity<TId>` (kimliğiyle tanımlanan nesne),
`AggregateRoot<TId>` (bir tutarlılık sınırının kök nesnesi, dışarıdan
sadece kök üzerinden erişilir), `ValueObject` (kimliksiz, değeriyle
eşitliği tanımlanan nesne — örn. `Email`, `Money`, `TenantSlug`),
`DomainEvent` (aggregate içinde gerçekleşen iş anlamı taşıyan bir
olayın kaydı, outbox'a yazılmadan önce handler içinde toplanır,
[ADR 0018](0018-outbox-pattern.md)).

## Consequences

### Positive
- Ubiquitous language (Evans'ın terimiyle: kod ile iş konuşmasının aynı
  kelimeleri kullanması) domain katmanında somutlaşır —
  "AggregateRoot" kod incelemesinde geçtiğinde herkes aynı şeyi anlar.
- Value Object'ler (Email, Money, TenantSlug) validasyonu kendi içine
  kapsüller: geçersiz bir Email asla oluşturulamaz, bu yüzden domain
  katmanının derinliklerinde "acaba bu string geçerli bir email mi"
  kontrolü tekrar tekrar yazılmaz.

### Trade-offs
- Her yeni kavram için "bu bir Entity mi yoksa Value Object mi" kararı
  verilmeli; bu küçük bir bilişsel yük ekler ama primitive obsession'ın
  uzun vadeli maliyetinden (validasyonun her yere dağılması) daha
  ucuzdur.

## Alternatives Considered

### Anemic domain model (sadece DTO benzeri sınıflar, tüm mantık service katmanında)
Daha az yapı, daha hızlı başlangıç — ama iş kuralları domain
nesnelerinden koparılıp service katmanına dağıldığında, aynı kuralın
birden fazla yerde tekrar yazılması veya unutulması riski artar. DDD'nin
temel eleştirisi tam da bu modele yönelik; bu proje domain
karmaşıklığının zamanla artacağı varsayımıyla (multi-tenant,
ledger/balance gibi hassas alanlar) baştan zengin domain model tercih
ediyor.
