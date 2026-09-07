# 12. ORM: EF Core Birincil, Dapper Performans-Kritik Sorgular İçin

**Status:** Accepted
**Date:** 2026-09-07

## Context

Domain modelinin ([ADR 0007](0007-domain-modeling-building-blocks.md))
veritabanına yazılması ve okunması için bir ORM/veri erişim stratejisi
gerekiyor. EF Core, .NET'in birincil ORM'i; change tracking, migration,
LINQ sorgu üretimi gibi geniş bir yüzey sunuyor. Ancak bazı okuma
senaryolarında (karmaşık rapor sorguları, yüksek performans gereken
listelemeler) EF Core'un ürettiği SQL, elle yazılmış bir sorgu kadar
optimize olmayabilir.

## Decision

EF Core, domain modelinin persist edilmesi ve genel CRUD/command tarafı
için birincil ORM olarak kullanılır. Performans kritik ham SQL
sorguları gerektiğinde (özellikle karmaşık query/rapor senaryolarında)
Dapper kullanılır. İkisi aynı `DbConnection` üzerinde, aynı proje
içinde birlikte kullanılabilir.

## Consequences

### Positive
- Command tarafı (yazma), EF Core'un change tracking ve `SaveChanges`
  ([ADR 0010](0010-hand-rolled-mediator-pipeline.md)'daki Unit of Work
  rolü) mekanizmasından tam olarak faydalanır.
- Query tarafı ([ADR 0009](0009-cqrs-lite.md)), ihtiyaç duyduğunda EF
  Core'un LINQ-to-SQL çeviri katmanını atlayıp Dapper ile elle optimize
  SQL yazabilir — bu iki yaklaşımı "ya hep ya hiç" seçmek zorunda
  kalmadan.

### Trade-offs
- İki farklı veri erişim yolu (EF Core LINQ ve Dapper ham SQL) bir arada
  var olduğu için, hangi senaryoda hangisinin kullanılacağına dair bir
  konvansiyon (varsayılan EF Core, ölçülmüş bir performans ihtiyacı
  varsa Dapper) takım içinde paylaşılmalı; aksi halde tutarsız bir kod
  tabanı oluşabilir.

## Alternatives Considered

### Sadece EF Core
En tutarlı, tek yol — ama karmaşık raporlama sorgularında EF Core'un
ürettiği SQL'i optimize etmek (özellikle çoklu join/aggregate içeren
sorgularda) LINQ ifadesini elle SQL'e çevirmekten daha zahmetli
olabilir.

### Sadece Dapper (EF Core yok, tüm erişim ham SQL)
Fowler'ın PoEAA'da tanımladığı Data Mapper deseninin (nesne modelini
veritabanı şemasından ayıran, EF Core'un sağladığı) avantajlarından
tamamen vazgeçmek anlamına gelirdi — her entity için elle mapping/
change-tracking kodu yazmak, domain modelinin
([ADR 0007](0007-domain-modeling-building-blocks.md)) zenginliğini
(Value Object'ler, aggregate sınırları) persist etmeyi çok daha emek
yoğun hale getirirdi.
