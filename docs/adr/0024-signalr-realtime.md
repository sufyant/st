# 24. Real-time: SignalR

**Status:** Accepted
**Date:** 2026-09-07

## Context

Gerçek zamanlı bildirim/güncelleme (örn. bir kayıt değiştiğinde diğer
açık sekmelere anlık yansıması) ihtiyacı var. Backend .NET olduğu için
real-time katmanının backend'in native ekosistemiyle uyumlu olması
tercih ediliyor.

## Decision

SignalR kullanılır (native .NET real-time kütüphanesi), Socket.IO
değil. Bildirimler outbox üzerinden değil, başarılı bir commit'in
hemen ardından doğrudan, aynı process içinde (in-process) tetiklenir
([ADR 0018](0018-outbox-pattern.md)) — açık bir bağlantının, email/
webhook'un aksine, dayanıklı (durable) teslimata ihtiyacı yok.

## Consequences

### Positive
- SignalR, ASP.NET Core'un bir parçası; authentication (Clerk JWT,
  [ADR 0003](0003-auth-authorization-split.md)), dependency injection,
  tenant/permission claim'leri
  ([ADR 0006](0006-permission-system.md)) ile doğal olarak entegre
  olur — ayrı bir Node.js tabanlı Socket.IO sunucusu çalıştırmaya, ayrı
  bir authentication köprüsü kurmaya gerek yok.
- Hub'lar (SignalR'ın bağlantı/mesajlaşma birimi) .NET tipleriyle güçlü
  tip güvenliğiyle yazılır; backend'in geri kalanıyla aynı dil, aynı
  derleme birimi.

### Trade-offs
- Socket.IO'nun JavaScript ekosistemindeki daha geniş topluluk desteği
  ve tarayıcı-dışı istemci kütüphaneleri (bazı platformlarda) SignalR'a
  göre daha yaygın olabilir; ancak istemciler zaten TypeScript/React
  (web) ve React Native (mobile) — her ikisi de resmi SignalR
  JavaScript/TypeScript client'ını destekliyor, bu bir pratik engel
  oluşturmuyor.

## Alternatives Considered

### Socket.IO
Popüler ve olgun, ama Node.js ekosisteminden geliyor — .NET backend'e
entegre etmek ya ayrı bir Node.js real-time servisi çalıştırmayı (ek
deployment birimi, ek authentication köprüsü) ya da üçüncü parti bir
.NET Socket.IO implementasyonuna (resmi değil, bakımı riskli) bağımlı
kalmayı gerektirirdi. SignalR, aynı ihtiyacı backend'in zaten native
desteklediği bir yoldan çözüyor.
