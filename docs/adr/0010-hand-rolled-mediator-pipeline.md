# 10. Mediator/Pipeline: MediatR Değil, Elle Yazılmış Pipeline

**Status:** Accepted
**Date:** 2026-09-07

## Context

Command/query handler'ların çağrılması (mediator deseni) ve bunların
etrafına logging/validation/permission gibi çapraz kesen davranışların
(cross-cutting concerns) eklenmesi (pipeline behavior deseni) gerekiyor.
MediatR, .NET ekosisteminde bu iş için en yaygın kullanılan kütüphane,
ancak 2025'te ticari lisansa geçti.

## Decision

MediatR kullanılmaz. Elle yazılmış, ince bir mediator (command/query'yi
ilgili handler'a yönlendiren) + pipeline behavior zinciri (logging,
validation, permission kontrolü) inşa edilir. Ayrı bir `UnitOfWork`
sınıfı yazılmaz — EF Core `DbContext`'in `SaveChangesAsync()` çağrısı,
pipeline'ın son adımı olarak zaten bu rolü üstlenir.

## Consequences

### Positive
- Ticari lisans bağımlılığı yok, gelecekte lisans koşulları değişse bile
  risk taşınmıyor.
- Mediator sadece bu projenin ihtiyaç duyduğu davranışları içerir
  (logging enrichment, FluentValidation, permission) — MediatR'ın genel
  amaçlı API yüzeyinin geri kalanı (notification pattern, streaming
  request'ler vb.) hiç var olmuyor, öğrenilecek/bakımı yapılacak yüzey
  küçük.
- Fowler'ın Patterns of Enterprise Application Architecture'da
  tanımladığı Unit of Work deseni zaten EF Core `DbContext`'in change
  tracking + `SaveChanges` mekanizmasının kendisi; ayrı bir `UnitOfWork`
  sınıfı yazmak, aynı sorumluluğu iki kez modellemek olurdu — DbContext'i
  saran ince bir UnitOfWork wrapper'ı, gerçek bir soyutlama sağlamadan
  sadece dolaylama (indirection) ekler.

### Trade-offs
- MediatR'ın topluluk tarafından test edilmiş, olgun kod tabanı yerine
  kendi pipeline'ının bakımı bu projenin sorumluluğunda; yeni bir
  cross-cutting concern eklemek MediatR'da bir behavior class'ı
  eklemekten biraz daha fazla elle iş gerektirebilir (ama yüzey küçük
  olduğu için bu maliyet düşük).

## Alternatives Considered

### MediatR (ticari lisans ile)
Küçük bir ekip için lisans maliyeti kabul edilebilir olabilirdi, ama
uzun vadeli bir starter kit'in temel bağımlılığını üçüncü parti bir
şirketin lisans politikasına bağlamak, projenin bağımsızlığını riske
atıyor.

### Ayrı bir `IUnitOfWork` arayüzü + implementasyonu
Test edilebilirlik için "repository + unit of work" deseni klasik bir
tavsiyedir, ama EF Core zaten kendi `DbContext`'i üzerinden bu iki rolü
(repository benzeri sorgu erişimi + unit of work) doğal olarak
sağlıyor. Fowler'ın kendisi de PoEAA'da bu pattern'lerin ORM'in zaten
sunduğu bir işlevi tekrar sarmalamak için değil, ORM'in sunmadığı bir
soyutlamayı sağlamak için kullanılması gerektiğini vurgular; burada EF
Core'un üzerine ince bir UnitOfWork eklemek gereksiz bir katman olurdu.
