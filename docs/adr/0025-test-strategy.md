# 25. Test Stratejisi: xUnit + Testcontainers, Day 1'den İtibaren

**Status:** Accepted
**Date:** 2026-09-07

## Context

Testsiz yazılan kod, Michael Feathers'ın "Working Effectively with
Legacy Code" kitabındaki tanımıyla, yazıldığı andan itibaren zaten
"legacy kod"dur — değiştirilmesi riskli, güvenle refactor edilemeyen
kod. Bu proje, güvenlik-kritik bir katman (tenant izolasyonu) içerdiği
için, testlerin sahte (SQLite in-memory gibi) değil gerçek bir
Postgres'e karşı çalışması önemli — SQLite'ın Postgres'ten farklı
davrandığı (örn. schema desteği, bazı SQL fonksiyonları, concurrency
davranışı) noktalarda sahte bir veritabanı yanlış güven verebilir.

## Decision

xUnit test framework'ü + Testcontainers (testler sırasında gerçek bir
Postgres container'ı ayağa kaldıran kütüphane) kullanılır, day 1'den
itibaren — "önce ürünü yaz, testi sonra ekle" değil. Tenant izolasyonunu
doğrulayan testler için ayrı, isimli bir `TenantIsolationTests` paketi
bulunur (bu, sistemin en kritik garantisinin kendi başına, açıkça
isimlendirilmiş bir yerde test edildiğini garanti eder). Testler AAA
yapısını (`// Arrange`, `// Act`, `// Assert`) kullanır — bu, projenin
genel "gereksiz comment yok" kuralının
([ADR 0027](0027-code-standard.md)) tek istisnasıdır.

## Consequences

### Positive
- Testcontainers ile gerçek Postgres kullanmak, Gerard Meszaros'un
  "xUnit Test Patterns" kitabındaki "Test the code you ship, not a
  substitute for it" ilkesiyle uyumlu — SQLite fake'in maskeleyebileceği
  Postgres'e özgü davranış farkları (schema izolasyonu,
  `SELECT ... FOR UPDATE SKIP LOCKED`, sequence davranışı) testlerde
  gerçekten ortaya çıkar.
- Ayrı, isimli `TenantIsolationTests` paketi, "tenant izolasyonu test
  edildi mi" sorusunun cevabının kod tabanında dağınık değil, tek bir
  bilinen yerde toplanmasını sağlar — Meszaros'un "Test Suite
  Organization" prensipleriyle uyumlu, kritik bir garantiyi kendi
  başına görünür kılan bir yapı.
- AAA yapısı, her testin "ne kuruluyor / ne çalıştırılıyor / ne
  doğrulanıyor" ayrımını görsel olarak netleştirir — bu üç bölümün
  karıştığı bir test, neyin test edildiğini anlamayı zorlaştırır; bu
  yüzden genel "yorum satırı yazma" kuralına (Clean Code'un vurguladığı,
  iyi isimlendirilmiş kodun kendini açıklaması gerektiği ilkesi) tek
  istisna olarak kabul ediliyor.

### Trade-offs
- Testcontainers, her test koşusunda gerçek bir Postgres container'ı
  ayağa kaldırdığı için, testler SQLite in-memory'ye göre daha yavaş
  çalışır; bu, doğruluk için bilinçli olarak kabul edilen bir maliyet.

## Alternatives Considered

### SQLite in-memory fake
Çok daha hızlı testler sağlar, ama Postgres'e özgü davranışları
(schema-per-tenant'ın dayandığı `search_path` mekanizması,
`SELECT ... FOR UPDATE SKIP LOCKED`, native sequence davranışı) test
edemez — bu proje için kritik olan tam olarak bu davranışlar. Feathers'ın
"legacy code" tanımının tersine, "yeşil ama yanlış güven veren testler",
hiç test olmamasından daha tehlikeli olabilir.

### Test'leri sonradan eklemek (feature-first, test-later)
Kısa vadede daha hızlı görünür, ama Feathers'ın merkezi tezi tam olarak
bunun bir yanılsama olduğu: testsiz yazılan kod, yazıldığı andan
itibaren güvenle değiştirilemez hale gelir ve sonradan test eklemek,
kodun zaten aldığı tasarım kararlarını (test edilebilirliği düşünmeden
yazılmış olması) geriye dönük düzeltmeyi gerektirir.
