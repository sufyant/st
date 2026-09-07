# apps/api icin ADR (Architecture Decision Record) seti

Tarih: 2026-09-07
Durum: onaylandi

## Amac

`apps/api` (.NET backend, henuz scaffold edilmemis) icin onceden alinmis
29 mimari kararin her birini ayri bir ADR dosyasina kaydetmek. Karar
icerikleri kullanici tarafindan zaten tam olarak verildi (context, decision,
elenen alternatifler dahil); bu tasarim dokumaninin isi asil kararlari
uretmek degil, bu 29 karari nasil dosyalara/gruplara ayiracagimizi ve hangi
sablonu kullanacagimizi sabitlemek.

Referans pratik: "Facilitating Software Architecture" (Andrew Harmel-Law)
kitabindaki ADR yaklasimi — her karar, gerekcesi ve elenen alternatifleriyle
birlikte kayit altina alinir.

## Kapsam

### Yeni dizin: `docs/adr/`

29 ADR dosyasi + `docs/adr/README.md` (indeks). Baska hicbir dosya
degismiyor — bu tamamen dokumantasyon eklemesi, kod tarafinda etkisi yok.

### Numaralandirma: tema bazli gruplama (kullaniciyla onaylandi)

Kullanicinin verdigi 29 karar, orijinal liste sirasindan tema bazli bir
siraya donduruldu (asagida her dosyanin yaninda orijinal numara parantez
icinde). Boylece README ust dokuzden okundugunda dogal bir anlati akisi
olusuyor: once tenancy/erisim, sonra domain/uygulama katmani, sonra
data/persistence, sonra API/entegrasyon, sonra muhendislik pratikleri.

**A. Tenancy & Access Control**
- `0001-tenant-isolation-schema-per-tenant.md` (orig #1)
- `0002-control-plane-admin-schema.md` (orig #2)
- `0003-auth-authorization-split.md` (orig #3)
- `0004-path-based-tenant-resolution.md` (orig #4)
- `0005-membership-and-invitation.md` (orig #5)
- `0006-permission-system.md` (orig #27)

**B. Domain & Application Layer**
- `0007-domain-modeling-building-blocks.md` (orig #16)
- `0008-dual-id-pattern.md` (orig #15)
- `0009-cqrs-lite.md` (orig #17)
- `0010-hand-rolled-mediator-pipeline.md` (orig #18)
- `0011-result-pattern.md` (orig #19)

**C. Data & Persistence**
- `0012-orm-ef-core-and-dapper.md` (orig #10)
- `0013-hand-rolled-tenant-middleware.md` (orig #11)
- `0014-schema-per-tenant-migrations.md` (orig #12)
- `0015-concurrency-control.md` (orig #14)
- `0016-utc-timezone-policy.md` (orig #22)
- `0017-caching-strategy.md` (orig #23)
- `0018-outbox-pattern.md` (orig #24)
- `0019-no-temporal-yet.md` (orig #25)
- `0020-no-quartz-yet.md` (orig #26)

**D. API & Integration**
- `0021-rest-api-versioning.md` (orig #7)
- `0022-generated-typescript-client.md` (orig #8)
- `0023-shared-api-surface.md` (orig #9)
- `0024-signalr-realtime.md` (orig #6)

**E. Engineering Practices & Platform**
- `0025-test-strategy.md` (orig #20)
- `0026-structured-logging.md` (orig #21)
- `0027-code-standard.md` (orig #28)
- `0028-dotnet-10-runtime.md` (orig #29)
- `0029-monorepo-tooling.md` (orig #13)

### ADR sablonu (her dosyada ayni yapi)

```markdown
# {N}. {Baslik}

**Status:** Accepted
**Date:** 2026-09-07

## Context
...

## Decision
...

## Consequences
### Positive
- ...
### Trade-offs
- ...

## Alternatives Considered
### {Alternatif 1}
Neden elendi...
```

Kullanicinin istedigi 5 alana (Title, Status, Context, Decision,
Consequences, Alternatives Considered) ek olarak sadece `Date` eklendi —
standart Nygard ADR pratiginin bir parcasi, karar tarihini iz surmek icin.

### Icerik dili

ADR icerikleri (docs/adr/*.md) tam Turkce (aksanli) yazilacak — bu,
kullanicinin kendi mesajinda kullandigi dil ve nihai, ekip tarafindan
okunacak bir dokumantasyon oldugu icin. Bu tasarim dokumaninin kendisi
(docs/superpowers/specs/*) ise bu repodaki mevcut spec dosyalarinin
konvansiyonuna (ASCII-Turkce) sadik kaliyor — iki farkli hedef kitle,
iki farkli konvansiyon.

### Kitap referanslarinin dagilimi

Referans kitaplar zorla her ADR'ye sikistirilmiyor, sadece dogal dustugu
yerde geciyor:

- **Ousterhout (A Philosophy of Software Design)** → agir secenegin neden
  elendigi gerekcelerinde: 0001 (DB-per-tenant), 0009 (full CQRS), 0010
  (MediatR), 0017 (genel cache katmani), 0019/0020 (Temporal/Quartz) —
  complexity/cognitive load cercevesiyle.
- **Evans (DDD)** → 0007 domain modeling (Entity/AggregateRoot/ValueObject/
  ubiquitous language terminolojisi).
- **Fowler (PoEAA)** → 0010 (DbContext = Unit of Work, ayri sinif
  gereksiz), 0012 (Repository/DbContext iliskisi).
- **Martin (Clean Code)** → 0027 kod standardi (gereksiz comment yok, AAA
  istisnasi).
- **Meszaros (xUnit Test Patterns)** → 0025 test stratejisi (yapi/
  isimlendirme).
- **Hunt & Thomas (Pragmatic Programmer)** → 0001 (DB-per-tenant'a gecis
  yolu acik), 0017 (ICachedQuery marker), 0019/0020 ("simdi karar verme,
  sonradan eklenebilir tasarla" ilkesi).
- **Feathers (Working Effectively with Legacy Code)** → 0025'te "testsiz
  kod = legacy kod" gerekcesi.
- **Diger arka plan kaynaklari** (Seven Languages in Seven Weeks, Learn
  You a Haskell for Great Good!, The Mythical Man-Month, C# in Depth) →
  zorunlu degil; sadece dogal dustugu 1-2 yerde kisa bir cumleyle (orn.
  Mythical Man-Month → 0001'de operasyonel maliyet tartismasinda; C# in
  Depth → 0008 dual ID / value type kararinda).

### `docs/adr/README.md`

Butun 29 ADR'yi yukaridaki 5 grup altinda, numara + kisa baslik + tek
cumlelik ozet ile listeleyen bir indeks. ADR pratiginin kendisini (nicin
bu format, hangi kitaptan geldigi) 2-3 cumleyle aciklayan kisa bir giris
de icerir.

## Kararlar ve gerekceleri

### Tema bazli gruplama, orijinal liste sirasi degil

Kullaniciya iki secenek sunuldu (liste sirasi vs tema grubu); tema grubu
secildi. Boylece README ust dokuzden okunan biri once "tenant nasil izole
ediliyor" sorusuna, sonra "domain nasil modelleniyor" sorusuna, sonra
"data/altyapi nasil calisiyor" sorusuna cevap buluyor — okuma sirasi
mimari anlati sirasiyla orustuyor. Orijinal numara her dosyanin yaninda
parantez icinde tutuluyor, boylece kullanicinin orijinal listesiyle
birebir izlenebilirlik kayip degil.

### Ingilizce dosya slug'lari

Kullanici Ingilizce slug'i tercih etti (`0001-tenant-isolation...` gibi).
Icerik Turkce kalirken dosya adlari Ingilizce teknik terimlerle — repo
genelinde link/grep atmak kolaylasiyor, coğu ADR pratiginde teknik slug
Ingilizce kalir.

### Ayri dosyalar, birlestirme yok

Kullanicinin verdigi 29 karar birebir 29 ayri dosyaya karsilik geliyor.
Bazi kararlar birbirine yakin gorunse de (orn. 0019/0020 — Temporal ve
Quartz.NET'in "simdilik eklenmedi" kararlari), her biri kendi basina
gelecekte bagimsiz olarak degisebilecek/genisleyebilecek bir karar oldugu
icin ayri tutuluyor — kullanicinin talebiyle de birebir uyumlu ("her biri
kendi dosyasi olsun").

## Kapsam disi

- ADR'lerin referans verdigi kod (apps/api) su an bos; bu gorev sadece
  dokumantasyon uretiyor, hicbir .NET kodu yazilmiyor.
- ADR sablonuna baglanti/cross-reference otomasyonu (orn. "related ADRs"
  alani) eklenmiyor — kullanici istemedi, YAGNI.
