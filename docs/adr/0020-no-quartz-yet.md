# 20. Quartz.NET: Şimdilik Gerekmiyor

**Status:** Accepted
**Date:** 2026-09-07

## Context

Quartz.NET, .NET'te cron-benzeri zamanlanmış işler (örn. "her gün saat
03:00'te şu raporu üret") için yaygın kullanılan bir kütüphane. Bu
projenin şu anki tek periyodik ihtiyacı outbox mesajlarının işlenmesi
([ADR 0018](0018-outbox-pattern.md)), ve bu ihtiyaç "belirli bir saatte
bir kez çalış" değil, "sürekli, kısa aralıklarla kuyruğu boşalt"
şeklinde — native `BackgroundService` + `PeriodicTimer`/`Task.Delay`
döngüsü bunun için yeterli.

## Decision

Quartz.NET şimdilik projeye eklenmiyor. Gerçek cron-benzeri zamanlanmış
işler (örn. "her gece raporu üret", "her ayın 1'inde faturaları kes")
ihtiyacı ortaya çıkarsa o zaman değerlendirilecek.

## Consequences

### Positive
- Outbox'ın ihtiyacı olan "sürekli kuyruk boşaltma" deseni zaten native
  `BackgroundService` ile tam olarak çözülüyor
  ([ADR 0018](0018-outbox-pattern.md)); Quartz.NET'in sunduğu cron
  expression, misfire-handling, job persistence gibi özelliklerin
  hiçbiri bugün kullanılmıyor olurdu.

### Trade-offs
- Gerçek bir cron ihtiyacı (örn. "her ayın 1'inde") ortaya çıktığında,
  `BackgroundService` içinde elle bir zamanlama mantığı yazmak (veya o
  noktada Quartz.NET eklemek) gerekecek; bu proje bilinçli olarak bu
  kararı erteliyor.

## Alternatives Considered

### Baştan Quartz.NET kurmak
Gelecekte gerekebilecek cron ihtiyaçlarına hazırlıklı olmak cazip
görünse de, bugün tek periyodik ihtiyaç (outbox) zaten daha basit bir
native mekanizmayla çözülüyor; Pragmatic Programmer'ın "ihtiyaç
doğduğunda ekle" ilkesiyle, kullanılmayan bir kütüphane bağımlılığı ve
öğrenme yüzeyi bugünden taşınmıyor.
