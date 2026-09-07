# 18. Outbox Pattern: Aynı Transaction, BackgroundService ile İşleme

**Status:** Accepted
**Date:** 2026-09-07

## Context

Bir command handler hem işletme verisini değiştirmeli hem de bu
değişikliğe bağlı bir yan etkiyi (örn. bir email gönderme isteği, bir
webhook tetikleme) güvenilir şekilde tetiklemeli. Bu iki işlemi ayrı transaction'larda yapmak, "işletme
verisi değişti ama yan etki hiç tetiklenmedi" (veya tam tersi)
senaryosuna açık kapı bırakır.

## Decision

Command handler, işletme değişikliğini ve bir outbox mesajını aynı
veritabanı transaction'ında yazar. Ayrı bir worker deploy etmek yerine,
aynı API process'i içinde çalışan bir `BackgroundService` (Quartz.NET
değil, .NET'in native background service mekanizması), her 5-10
saniyede bir `SELECT ... FOR UPDATE SKIP LOCKED` ile bekleyen outbox
mesajlarını okuyup işler.

## Consequences

### Positive
- "Business değişikliği yazıldı ama yan etki kayboldu" senaryosu
  imkansız hale gelir — ikisi aynı transaction'da yazıldığı için ya
  ikisi de commit olur ya hiçbiri.
- `SELECT ... FOR UPDATE SKIP LOCKED`, birden fazla API instance'ı aynı
  anda çalışırken (yatay ölçekleme), her instance'ın kendi
  `BackgroundService`'i aynı outbox tablosunu okusa bile, bir mesajı
  zaten işlemekte olan instance'ın kilitlediği satırı diğerlerinin
  atlamasını (skip) sağlar — bu, Quartz.NET'in clustering özelliğinin
  çözdüğü "aynı işi birden fazla worker'ın aynı anda işlememesi"
  problemini, ekstra bir clustering kütüphanesi/konfigürasyonu olmadan
  native Postgres mekanizmasıyla çözer.
- Ayrı bir worker deployment'ı olmadığı için, deploy/operasyon yüzeyi
  tek bir API process'inde kalır.

### Trade-offs
- Outbox mesajları gerçek zamanlı değil, en fazla 5-10 saniyelik bir
  gecikmeyle işlenir — gerçek zamanlı gereksinimi olan senaryolar
  (varsa) bu deseni kullanmamalı. Bu yüzden SignalR gibi düşük gecikmeli,
  in-process bildirimler outbox üzerinden değil, commit sonrası eş
  zamanlı (senkron) olarak doğrudan tetiklenir
  ([ADR 0024](0024-signalr-realtime.md)).
- `BackgroundService`, API process'iyle aynı yaşam döngüsünü paylaşır;
  API process'i deploy sırasında yeniden başlatıldığında, o anda
  işlenmekte olan bir mesaj yarım kalabilir (bu yüzden
  `SELECT ... FOR UPDATE SKIP LOCKED` + idempotent işleme mantığı
  birlikte kullanılmalı).

## Alternatives Considered

### İki ayrı transaction (business değişikliği + doğrudan yan etki tetikleme)
En basit görünen yaklaşım, ama yan etkinin tetiklenmesi (örn. bir email
servisi çağrısı) başarısız olursa veya process o anda çökerse, business
değişikliği commit olmuş ama yan etki hiç gerçekleşmemiş olur — sessiz
veri tutarsızlığı.

### Ayrı bir worker deployment'ı + Quartz.NET clustering
Daha "endüstri standart" bir yaklaşım ama outbox'ın ihtiyacı gerçek bir
cron zamanlaması değil, sürekli kuyruk boşaltma
([ADR 0020](0020-no-quartz-yet.md)); ayrı bir deployment birimi +
clustering konfigürasyonu, bugünkü ölçek için gereksiz operasyonel
karmaşıklık ekler.
