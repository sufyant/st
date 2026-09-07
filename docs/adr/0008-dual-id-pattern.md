# 8. Dual ID Deseni: İç GUID + Dışa Açık Sıralı Public ID

**Status:** Accepted
**Date:** 2026-09-07

## Context

Veritabanı içi ilişkiler için performanslı, index-dostu bir birincil
anahtar gerekiyor; dışarıya (URL, fatura numarası, kullanıcı arayüzü)
ise kullanıcının okuyup telaffuz edebileceği, sıralı bir tanımlayıcı
gerekiyor (örn. `INV-000042`). Bu iki ihtiyaç aynı alanla karşılanamaz.

## Decision

İç PK/FK için `Guid` kullanılır. Dışa açık, okunabilir "public ID" (örn.
`INV-000042`), her tenant schema'sı içinde tanımlı bir PostgreSQL
`SEQUENCE`'tan üretilir ve entity oluşturulurken atanır.

## Consequences

### Positive
- Postgres `SEQUENCE`, veritabanı düzeyinde native olarak
  concurrency-safe'tir — iki eşzamanlı insert asla aynı sequence
  değerini almaz, manuel lock veya "SELECT MAX(...)+1" gibi race
  condition'a açık desenlere gerek kalmaz.
- Guid PK, tenant şemaları arası veri taşıma/senkronizasyonda (örn.
  ileride DB-per-tenant'a geçişte,
  [ADR 0001](0001-tenant-isolation-schema-per-tenant.md)) çakışma riski
  taşımaz.
- Sequence tenant schema'sı içinde tanımlı olduğu için, her tenant kendi
  `INV-000001`'inden başlar — tenant'lar arası public ID sızıntısı/
  tahmin edilebilirlik riski (bir tenant'ın kaç kaydı olduğunu diğer
  tenant'ın numarasından çıkarmak) oluşmaz.

### Trade-offs
- Her aggregate'in hem Guid Id'si hem public ID'si taşınır; hangi
  bağlamda hangisinin kullanılacağı (iç ilişkiler → Guid, dış arayüz →
  public ID) bir konvansiyon olarak öğrenilmeli. C# in Depth'in (Jon
  Skeet) vurguladığı gibi, dilin sunduğu `record struct` gibi hafif
  tipler bu iki ID'yi birbirine karışmayacak şekilde (örn. `InvoiceId`
  value type'ı) modellemek için kullanılabilir.

## Alternatives Considered

### Tek bir ID (sadece Guid, public ID yok)
En basit, ama Guid'ler kullanıcı arayüzünde veya destek görüşmesinde
("faturanız 7f3a9c12-..." demek) telaffuz edilemez ölçüde kullanışsız.

### Tek bir ID (sadece sıralı integer, dışa da açık)
Sıralı integer PK, dışarıya tenant'ın toplam kayıt sayısını sızdırır
(bir rakibin `INV-000042`'den kaç fatura kesildiğini tahmin etmesi
gibi) ve dağıtık/çok-şemalı bir sistemde PK çakışma riski taşır.
