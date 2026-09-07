# apps/api Phase 2a: Domain building blocks

Date: 2026-09-07
Status: approved (autonomous run, no interactive review — see phase ledger)

## Amaç

[ADR 0007](../../adr/0007-domain-modeling-building-blocks.md)'de karar
verilen `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent`
base class'larını ve primitive obsession'dan kaçınmak için örnek Value
Object'leri (`Email`, `Money`, `TenantSlug`) `Api.Domain` projesine
ekler. Bu faz saf domain katmanı — EF Core yok, veritabanı yok,
Testcontainers gerekmiyor; sadece xUnit unit testleri (mevcut
`Api.Tests.Unit` projesi, `Domain/` alt klasörü).

## Result<T> ile ilişkisi (sıralama notu)

[ADR 0011](../../adr/0011-result-pattern.md) beklenen iş hatalarında
`Result<T>` kullanılmasını öngörüyor, ama `Result<T>`'in kendisi ledger'da
Faz 4'e (CQRS-lite + pipeline) atanmış. Bu fazda Value Object'lerin
`Create` factory metodları bu yüzden `Result<T>` yerine `ArgumentException`
fırlatır — bu, ADR 0011'in ruhuyla çelişmiyor: ADR 0011'in kendi
gerekçesi "beklenen iş hataları" için `Result<T>` istiyor, ama input
validasyonu zaten pipeline'da FluentValidation ile handler'a ulaşmadan
yapılıyor ([ADR 0006](../../adr/0006-permission-system.md)'daki sıra:
Validation → Handler). Bir Value Object constructor'ının bu noktada
başarısız olması, "beklenmeyen, gerçekten istisnai" bir invariant ihlali
sayılır — tam olarak ADR 0011'in exception'a izin verdiği durum. Faz 4
`Result<T>`'i eklediğinde, Application katmanındaki handler'lar Value
Object factory'lerini `try/catch` yerine kendi validasyonlarıyla
koruyacak; Value Object'lerin kendisi değişmeyecek.

## Eklenecek dosyalar

`apps/api/src/Api.Domain/`:
- `Entity.cs` — `Entity<TId>`, kimlik bazlı eşitlik.
- `AggregateRoot.cs` — `AggregateRoot<TId> : Entity<TId>`, domain event
  toplama (`Raise`, `DomainEvents`, `ClearDomainEvents`).
- `DomainEvent.cs` — soyut `record DomainEvent`, `OccurredOnUtc`
  (UTC — [ADR 0016](../../adr/0016-utc-timezone-policy.md)).
- `ValueObject.cs` — yapısal eşitlik (`GetEqualityComponents`).
- `Email.cs`, `Money.cs`, `TenantSlug.cs` — `ValueObject`'ten türeyen,
  private constructor + `Create` static factory ile invariant'larını
  kendi içine kapsülleyen value type'lar.

`apps/api/tests/Api.Tests.Unit/Domain/`:
- `EntityTests.cs`, `AggregateRootTests.cs`, `EmailTests.cs`,
  `MoneyTests.cs`, `TenantSlugTests.cs`.

## Kapsam dışı

- EF Core / persistence (Faz 2b).
- Gerçek domain entity'leri (örn. `Tenant`, `User`, `Membership`) — bu
  faz sadece base class'ları ve örnek Value Object'leri kurar, Faz 2b
  bunları kullanarak gerçek entity'leri yazar.
- `Result<T>` (Faz 4).

## Kabul kriterleri

1. `Api.Domain` hâlâ hiçbir dış pakete bağımlı değil (sadece BCL) —
   `Api.Domain.csproj`'a `PackageReference` eklenmez.
2. `pnpm --filter api test` başarılı, yeni domain testleri dahil tüm
   testler geçiyor.
3. `Email`, `Money`, `TenantSlug` geçersiz girdide `ArgumentException`
   fırlatıyor; geçerli girdide doğru değeri taşıyor; iki eşit değerli
   instance yapısal olarak eşit (`==`).
