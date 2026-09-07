# 28. Backend Runtime: .NET 10 (LTS)

**Status:** Accepted
**Date:** 2026-09-07

## Context

Yeni bir backend projesi için hangi .NET sürümünün hedefleneceğine
karar verilmeli. .NET, her yıl bir sürüm çıkarır; çift sayılı sürümler
(örn. .NET 8, 10) LTS (Long Term Support, 3 yıl destek), tek sayılı
sürümler (örn. .NET 9) STS (Standard Term Support, 18 ay destek)
statüsündedir. .NET 10, Kasım 2025'te çıkan güncel LTS sürümü.

## Decision

Backend, .NET 10 (LTS) üzerinde çalışır.

## Consequences

### Positive
- 3 yıllık destek süresi, projenin runtime güncellemesi konusunda acele
  etmeden, güvenlik yamalarını almaya devam edebileceği geniş bir
  pencere sağlıyor — bu, henüz ürünü/ölçeği netleşmemiş bir starter kit
  için özellikle değerli, çünkü proje büyürken runtime migration'ı
  gündeme almak zorunda kalmıyor.
- Kasım 2025'te çıkan en güncel LTS olduğu için, .NET 10'daki en yeni
  dil/runtime iyileştirmelerinden (performans, C# dil özellikleri)
  baştan faydalanılıyor.

### Trade-offs
- Çok yeni bir sürüm olduğu için, üçüncü parti kütüphanelerin (örn.
  Testcontainers, Serilog sink'leri, Dapper) .NET 10 uyumluluğu zamanla
  netleşecek; bu risk, LTS sürümlerinin genelde hızlı ekosistem desteği
  alması nedeniyle düşük görülüyor.

## Alternatives Considered

### .NET 9 (STS)
.NET 10'dan birkaç ay önce çıkmış olsa da, sadece 18 ay destekleniyor —
yeni bir projeyi kısa ömürlü bir sürüme sabitlemek, ürün henüz
olgunlaşmadan bir runtime migration'ını gündeme getirme riski taşır.
Yeni başlayan bir proje için LTS'in sunduğu 3 yıllık istikrar, STS'in
birkaç aylık "daha yeni sürüm" avantajından daha değerli görüldü.

### .NET 8 (önceki LTS)
Daha olgun, daha geniş ekosistem desteği var ama .NET 10 zaten çıkmış ve
LTS statüsünde olduğu için, bilerek daha eski bir LTS'e sabitlenmenin
(10'un getirdiği iyileştirmelerden faydalanmama pahasına) bir gerekçesi
yok.
