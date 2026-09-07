# 15. Concurrency Kontrolü: Yüksek Çakışmada Pessimistic, Düşük Çakışmada Optimistic

**Status:** Accepted
**Date:** 2026-09-07

## Context

Farklı senaryolar farklı eşzamanlılık risklerine sahip. Bakiye/ledger
gibi alanlarda çok sayıda eşzamanlı yazma aynı satıra çarpabilir (yüksek
çakışma); kullanıcı profili düzenleme gibi senaryolarda ise çakışma
nadirdir (düşük çakışma) ama yine de "iki kullanıcı aynı anda aynı
kaydı düzenlerse ne olur" sorusu cevaplanmalı.

## Decision

Yüksek çakışma beklenen senaryolarda (balance/ledger gibi) pessimistic
locking kullanılır (`SELECT ... FOR UPDATE`) — işlem satırı kilitler,
diğer eşzamanlı işlemler bu satır serbest kalana kadar bekler. Düşük
çakışma/kullanıcı düzenleme senaryolarında optimistic concurrency
kullanılır (EF Core `RowVersion`) — çakışma nadiren olacağı için
kilitlemenin maliyetine katlanmak yerine, çakışma gerçekleştiğinde
hatayı yakalayıp kullanıcıya bildirmek tercih edilir. İkisi projede
birlikte, senaryoya göre kullanılır.

## Consequences

### Positive
- Ledger gibi finansal doğruluğun kritik olduğu alanlarda, pessimistic
  locking "kayıp güncelleme" (lost update) riskini veritabanı düzeyinde,
  uygulama kodunun disiplinine bırakmadan ortadan kaldırır.
- Düşük çakışmalı senaryolarda optimistic concurrency, gereksiz
  kilitleme maliyetinden kaçınarak throughput'u yüksek tutar;
  RowVersion, EF Core'un native desteklediği bir mekanizma, ekstra
  kütüphane gerekmiyor.

### Trade-offs
- İki farklı concurrency stratejisinin bir arada var olması, "bu
  handler için hangi strateji doğru" sorusunun her yeni yazma
  senaryosunda düşünülmesini gerektirir — bu bilinçli bir maliyet,
  çünkü yanlış strateji seçimi (örn. ledger'a optimistic concurrency
  uygulamak) sessiz veri bütünlüğü hatalarına yol açabilir.

## Alternatives Considered

### Her yerde tek bir strateji (sadece optimistic veya sadece pessimistic)
Sadece optimistic: ledger gibi yüksek çakışmalı senaryolarda kullanıcılar
sürekli "conflict" hatası alır ve retry etmek zorunda kalır — kötü UX
ve gerçek bir doğruluk riski (retry mantığı doğru yazılmazsa). Sadece
pessimistic: düşük çakışmalı senaryolarda (örn. kullanıcı profil
düzenleme) gereksiz kilitleme, throughput'u düşürür ve deadlock riskini
artırır. İki farklı senaryonun tek bir stratejiyle çözülmesi, birinde
performansı birinde doğruluğu feda eder.
