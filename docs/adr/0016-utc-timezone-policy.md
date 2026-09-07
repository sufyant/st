# 16. Timezone: Backend/DB Her Zaman UTC

**Status:** Accepted
**Date:** 2026-09-07

## Context

Multi-tenant, potansiyel olarak farklı coğrafyalardaki kullanıcılara
hizmet veren bir sistemde, zaman verisinin tutarlı bir referans noktası
olmadan (örn. bazı kayıtlar local time, bazıları UTC) doğru sıralama,
karşılaştırma ve raporlama imkansız hale gelir.

## Decision

Backend ve veritabanı her zaman UTC kullanır (Postgres `timestamptz`,
.NET `DateTimeOffset`; naive `DateTime` kullanılmaz). İstemci,
kullanıcının yerel zamanını UTC'ye çevirip gönderir. `created_at`/
`updated_at` gibi alanlar asla istemciden gelmez — backend, işlemi
işlerken `UtcNow` ile üretir. Kullanıcının IANA timezone kimliği (örn.
`Europe/Istanbul`) `admin` schema'sındaki `Users` tablosunda
([ADR 0002](0002-control-plane-admin-schema.md)) saklanır, görüntüleme
sırasında UTC→local çevrimi için kullanılır.

## Consequences

### Positive
- Tüm zaman verisi tek bir referans noktasında (UTC) olduğu için,
  farklı tenant'lardaki/coğrafyalardaki kayıtları karşılaştırmak,
  sıralamak, raporlamak trivial hale gelir — "hangi timezone'da
  kaydedilmiş" sorusu hiç sorulmaz.
- `created_at`/`updated_at`'in her zaman backend tarafından üretilmesi,
  istemcinin yanlış ayarlanmış saatinin veya kötü niyetli bir isteğin
  denetim kaydını (audit trail) bozmasını engeller.

### Trade-offs
- Her UI bileşeni, kullanıcıya zaman gösterirken UTC→local çevrimini
  doğru yapmalı; bu çevrimi unutan bir ekran, kullanıcıya yanlış saat
  gösterebilir — bu risk, çevrimin tek bir merkezi yardımcı fonksiyon/
  hook üzerinden yapılmasıyla azaltılır.

## Alternatives Considered

### Her tenant'ın kendi timezone'unda saklamak
Tenant'lar arası karşılaştırma ve raporlamayı karmaşıklaştırır; bir
tenant'ın timezone'u değişirse (örn. yaz saati uygulaması olan bir
ülkeden olmayan birine taşınırsa) geçmiş kayıtların yorumlanması
belirsizleşir. UTC + kullanıcı bazlı IANA timezone, zaman verisinin
"ne zaman oldu" (UTC, değişmez gerçek) ile "kullanıcıya nasıl
gösterilecek" (görüntüleme kaygısı) sorumluluklarını net şekilde ayırır.
