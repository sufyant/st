# 5. Membership & Davet: Çoklu-Tenant Üyelik Modeli

**Status:** Accepted
**Date:** 2026-09-07

## Context

Bir kullanıcı birden fazla tenant'a üye olabilmeli (örn. bir freelance
muhasebeci birden fazla müşteri firmasının workspace'ine erişebilir).
Tenant'a yeni kullanıcı eklemenin yolu davet akışından geçmeli.

## Decision

`Memberships` tablosu (user_id, tenant_id, role) `admin` schema'sında
kullanıcı-tenant ilişkisini ve rolünü tutar — ayrı bir `status` kolonu
yok, satırın varlığı zaten aktif üyelik anlamına gelir. `Invitations`
tablosu (email, tenant_id, role, token) bekleyen davetleri tutar; davet
kabul edildiğinde `Memberships`'e satır eklenir. Bir kullanıcı tenant'tan
çıkarıldığında ilgili `Memberships` satırı silinir. Web istemcisinde bir
workspace switcher bulunur: kullanıcı tek tenant'a üyeyse otomatik o
tenant'a yönlendirilir, birden fazlaysa bir seçim ekranı gösterilir.

## Consequences

### Positive
- Kullanıcı-tenant ilişkisi normalize bir tabloda, tek bir yerden
  sorgulanabilir; "bu kullanıcı hangi tenant'lara üye" veya "bu
  tenant'ın üyeleri kim" sorguları basit bir JOIN.
- Davet akışı, henüz Clerk'te hesabı olmayan bir email'e de gönderilebilir
  (token bazlı kabul akışı); kullanıcı daveti kabul ettiğinde hesabı
  yoksa önce Clerk'te oluşturur, sonra `Memberships`'e satır eklenir.
- Ayrı bir `status` alanı olmadığı için, her membership sorgusunda
  "pending mi active mi" diye filtrelemeyi unutma riski hiç doğmuyor —
  bir satır varsa üyelik zaten aktiftir; `Invitations` ve `Memberships`
  arasındaki sorumluluk ayrımı (biri "henüz kabul edilmedi", diğeri
  "kabul edildi") tablo şemasının kendisinde açık.

### Trade-offs
- Bir kullanıcı tenant'tan çıkarıldığında satır silindiği için,
  "bu kişi daha önce bu tenant'a üyeydi" bilgisi `Memberships`
  tablosunda tutulmuyor; geçmiş üyelik kaydı gerekirse ayrı bir audit/
  event log ile çözülür (bu ADR'nin kapsamı dışında).
- Üyeliği tamamen silmeden geçici olarak devre dışı bırakma ("suspend")
  ihtiyacı bugün yok; gerçek bir ihtiyaç ortaya çıkarsa bu, tabloya
  eklenecek ayrı, açıkça isimlendirilmiş bir alanla (örn.
  `SuspendedAt`) çözülür — bugünden genel bir `status` state machine'i
  ile öngörülmüyor.

## Alternatives Considered

### Kullanıcı başına tek tenant (Clerk Organizations'ın varsayılan modeline benzer)
Basit ama bu projenin hedeflediği kullanım senaryosuyla (aynı kişinin
birden fazla workspace'e erişmesi) uyuşmuyor; sonradan çoklu-tenant
desteğine geçmek, membership tablosunu ve tüm auth claim üretim
mantığını yeniden yazmayı gerektirirdi.

### `Memberships` üzerinde ayrı bir `status` kolonu (pending/active/removed)
İlk taslakta değerlendirildi, ama `Invitations` tablosu zaten "henüz
kabul edilmedi" durumunu taşıyor; `Memberships` üzerinde bunu ikinci kez
bir `status` alanıyla modellemek gereksiz bir state machine ekliyor ve
her okuma sorgusunun bu alanı doğru filtrelemesini gerektiriyor — bir
geliştiricinin bu filtreyi unutması, henüz kabul edilmemiş bir daveti
aktif üyelik gibi göstermesine yol açabilir. Satırın varlığının kendisi
zaten "aktif" anlamına geldiğinde, bu risk hiç doğmuyor.

### `status`'u kullanıcı-tenant ilişkisi yerine tenant'ın kendi schema'sında tutmak
Bir üyeliğin var olup olmadığı sorusu tanım gereği bir control-plane
sorusu — "bu kullanıcı bu tenant'a erişebilir mi" sorusunun cevabı,
tenant'ın kendi verisinden değil, `admin` schema'sındaki merkezi
kayıttan gelmeli
([ADR 0002](0002-control-plane-admin-schema.md)). Bu bilgiyi tenant
schema'sına taşımak, control-plane ile tenant-data arasında bilinçli
olarak çizilen sınırı bulanıklaştırır.
