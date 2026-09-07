# 14. Migration: EF Core Code-First, Deploy Sırasında Tüm Tenant Şemalarına Uygulama

**Status:** Accepted
**Date:** 2026-09-07

## Context

Schema-per-tenant modelinde
([ADR 0001](0001-tenant-isolation-schema-per-tenant.md)) her tenant
kendi Postgres şemasına sahip; bir migration (örn. yeni bir kolon
eklemek) tüm tenant şemalarına uygulanmalı, sadece bir tanesine değil.

## Decision

EF Core code-first migration'lar kullanılır. Deploy sürecinde,
`admin.tenants` tablosundaki tenant listesi gezilir ve her tenant
schema'sı için `DbContext`'in `HasDefaultSchema` değeri o tenant'ın
schema adına dinamik olarak ayarlanarak migration uygulanır. Bu
`HasDefaultSchema` mekanizması yalnızca migration zamanında, deploy
sırasında tenant başına oluşturulan kısa ömürlü bir `DbContext`
örneği için kullanılır; runtime'da istek işleyen `DbContext` bundan
ayrıdır ve şema seçimi için
[ADR 0001](0001-tenant-isolation-schema-per-tenant.md)'deki
`search_path` mekanizmasına dayanır.

## Consequences

### Positive
- Tek bir migration seti (kod tabanında bir kez tanımlı) tüm tenant'lara
  tutarlı şekilde uygulanır; şema drift'i (bazı tenant'ların eski,
  bazılarının yeni şemada kalması) riski merkezi bir deploy adımıyla
  azaltılır.
- EF Core'un code-first migration sistemi zaten olgun ve iyi
  belgelenmiş; sadece "hangi schema'ya uygulanacağı" dinamikleştiriliyor,
  migration'ın kendisi standart EF Core akışında kalıyor.

### Trade-offs
- Tenant sayısı arttıkça, deploy sırasında N tenant için sırayla
  migration uygulamak deploy süresini uzatır; bu, kabul edilen bir
  başlangıç maliyeti — tenant sayısı büyürse bu adım bir worker/kuyruğa
  (arka planda, deploy'u bloklamadan çalışan) evrilebilir. Pragmatic
  Programmer'ın "bugün en basit çözümü seç, ama genişleyebilir bırak"
  ilkesiyle uyumlu olarak, bugün senkron bir deploy adımı yeterli,
  mimari bunu yarın asenkron bir işleme dönüştürmeyi engellemiyor.

## Alternatives Considered

### Her tenant için ayrı migration geçmişi/branch'i
Tenant'lar arası şema tutarlılığını garanti etmez, zamanla her tenant
farklı bir şema versiyonunda kalabilir — bu, "tüm tenant'lar aynı kod
tabanını çalıştırır" varsayımını kırar ve bug/destek yükünü katlar.
