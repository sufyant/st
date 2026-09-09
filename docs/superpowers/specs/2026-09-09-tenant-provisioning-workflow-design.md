# Spec 2: Tenant Provisioning Workflow

**Tarih:** 2026-09-09
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api`
**Önkoşul:** [Spec 1 — Control Plane Temeli](2026-09-09-control-plane-temeli-design.md)

## Bağlam

Spec 1, control plane'in temelini tanımladı: tenant kaydının şekli, yaşam döngüsü durumu,
veritabanı rolleri, ayrı migrator projesi ve provisioning ile migration arasındaki sınır.
Ancak bir tenant'ı gerçekten var eden şey henüz yok — `control.tenants`'a bir satır yazmak,
o tenant'ın veritabanını, şemasını, yetkilerini ve ilk kullanıcısını oluşturmuyor.

Bu spec o boşluğu doldurur: bir platform admininin tenant oluşturma isteğinden, o tenant'a
giriş yapılabilir hale gelmesine kadar geçen akışı tanımlar.

Provisioning senkron yapılmaz. Çok adımlı, saniyeler süren ve yarıda patlayabilen bir iştir;
işlemi yapan process ortada ölürse senkron tasarımda kurtarılamaz bir yarım tenant kalır.
Bunun yerine iş, `control_plane` içindeki bir outbox mesajı üzerinden arka planda yürütülür.

Bu aynı zamanda outbox deseninin projedeki ilk gerçek kullanımıdır: ayrı bir mekanizma
kurmak yerine, zaten kurulması planlanan outbox provisioning ile hayata geçer.

## Kapsam

### Bu spec'te

- `control_plane` içinde outbox tablosu ve arka plan worker'ı
- Provisioning workflow'unun adımları, idempotency'si ve başarısızlık davranışı
- Tenant kaydında ilerleme takibi
- Tenant oluşturma, listeleme, detay ve yeniden deneme endpoint'leri
- İlk owner kullanıcısının kurulması

### Bu spec'te değil

- Invitations ve mevcut bir tenant'a sonradan kullanıcı ekleme (Spec 3)
- Admin arayüzü — `apps/admin` boş bir dizindir, bu spec yalnızca API tarafını kapsar
- Tenant veritabanlarında outbox tablosu (ilk tenant iş olayı çıktığında)
- CQRS, mediator, pipeline behavior'lar, `Result<T>`

## Kararlar

### 1. Rollerin yaşadığı yer

Bir kullanıcının bir tenant'taki rolü **tenant'ın kendi veritabanında** yaşar. `control_plane`
içindeki membership kaydı yalnızca bir kapıdır: "bu kullanıcı bu tenant'a girebilir". Roller,
permission'lar ve kullanıcı-rol atamaları tenant veritabanındaki `TenantRole`,
`TenantRolePermission`, `TenantUserRole` tablolarındadır.

Gerekçe: bir tenant'ın yetkilendirme verisi kendi veritabanıyla birlikte yedeklenir, taşınır
ve dışa aktarılır — database-per-tenant mimarisinin asıl kazancı budur. Yetkiyi control
plane'e taşımak, tenant'ın verisini eksik bırakır. Ayrıca her tenant'ın ileride kendi
rollerini tanımlayabilmesinin yolu açık kalır.

Middleware zaten kullanıcı durumu için tenant veritabanına gittiğinden ek bir tur maliyeti
yoktur.

Not: Projenin eski karar listesindeki 5. ve 27. maddeler rollerin `admin` şemasında
yaşadığını söylüyor. Bunlar 1. madde gibi bayattır; mevcut kod ve `apps/api/AGENTS.md` bu
karara uygundur.

### 2. İlk owner

Tenant'ı oluşturan platform admin, o tenant'ın ilk owner'ı olur. Kişinin Clerk kimliği
isteğin JWT'sinde zaten mevcuttur; provisioning onun `TenantUser` kaydını, owner rolü
atamasını ve control plane'deki membership'ini oluşturur.

Gerekçe: bu spec'in davet mekanizmasına bağımlı olmamasını sağlar ve tenant hazır olur olmaz
giriş yapılıp test edilebilmesini mümkün kılar. Spec 3 geldiğinde gerçekçi davet akışı bunun
yanına eklenir; bu yol bootstrap seçeneği olarak kalır.

Platform yetkisi ile tenant rolü ayrımı korunur: kişi burada iki ayrı sıfat taşır, biri
diğerini doğurmaz.

### 3. Workflow yapısı

Tek outbox mesajı, tek handler, adımlar handler içinde sırayla yürütülür.

Değerlendirilen alternatifler:

- **Adım başına mesaj zinciri:** her adım kendi mesajı ve handler'ı olur, tek tek yeniden
  denenir. Dallanması ve paralelliği olmayan beş adımlık düz bir akış için beş mesaj tipi ve
  beş handler gereksiz karmaşıklıktır; akış dosyalara dağılır ve sırayı görmek zorlaşır.
- **Reconciliation loop:** worker terminal durumda olmayan tenant'ları periyodik tarar ve bir
  adım ileri iter. Kendi kendini iyileştirir, ancak event-driven değildir ve provisioning
  outbox'ı hiç kullanmamış olur. Takılan tenant'ları toparlama ihtiyacı gerçekten doğarsa
  seçilen tasarımın üstüne küçük bir güvenlik ağı olarak eklenebilir.

### 4. Outbox tablosunun konumu

Outbox'ın temel kuralı, iş değişikliği ile mesajın aynı transaction'da yazılmasıdır; bu,
tablonun iş verisiyle aynı veritabanında olmasını zorunlu kılar.

Provisioning bir control plane olayı olduğu için tablo yalnızca `control_plane` içinde
oluşturulur. İleride tenant iş olayları çıktığında her tenant veritabanına aynı şemayla bir
outbox tablosu eklenir ve aynı worker kodu kullanılır. O gün gelene kadar yazılmaz.

### 5. `control.outbox_messages`

```
id               uuid, PK
type             text                mesaj tipi, örn. "TenantProvisioningRequested"
payload          jsonb
created_at       timestamptz
processed_at     timestamptz null    NULL ise henüz işlenmedi
attempt_count    int, default 0
next_attempt_at  timestamptz         üstel backoff
last_error       text null
```

Ayrı bir status kolonu yoktur: `processed_at IS NULL` beklemede anlamına gelir. Deneme üst
sınırını aşan mesajlar sorguya düşmez ve `attempt_count >= 5` filtresiyle görünür.

### 6. Worker

`BackgroundService` olarak API process'i içinde çalışır, on saniyede bir yoklar. Ayrı bir
worker deployment'ı yoktur.

```sql
SELECT * FROM control.outbox_messages
WHERE processed_at IS NULL
  AND attempt_count < 5
  AND next_attempt_at <= now()
ORDER BY created_at
LIMIT 10
FOR UPDATE SKIP LOCKED
```

`SKIP LOCKED`, birden fazla API instance'ı çalıştığında aynı mesajın iki kez alınmasını
engeller. Bu, harici bir zamanlayıcının clustering'ine gerek bırakmayan tasarımın
dayanağıdır.

Mesajlar `type` alanına göre handler'a yönlendirilir. Başarılı işlemede `processed_at`
yazılır ve commit edilir.

### 7. İki farklı başarısızlık

**Handler exception fırlatırsa:** `attempt_count` artırılır, `last_error` ve üstel backoff'lu
`next_attempt_at` yazılır ve **bu kayıt commit edilir**. İş geri alınır ama denemenin kaydı
kalır; aksi halde mesaj iz bırakmadan sonsuza kadar yeniden denenirdi.

**Process ortada ölürse:** hiçbir şey commit edilmez, kilit bırakılır ve mesaj kendiliğinden
yeniden alınabilir hale gelir. Çökme kurtarması ek bir mekanizma gerektirmez.

`CREATE DATABASE` PostgreSQL'de transaction bloğu içinde çalışamadığı için asıl iş zaten
outbox transaction'ının dışında, ayrı bir bağlantıda yürütülür. Outbox transaction'ı yalnızca
mesaj satırını tutar; uzun süren işlerde kilidin dar kalmasını da bu sağlar.

### 8. İlerleme takibi

`control.tenants` tablosuna iki kolon eklenir:

```
provisioning_step   text null    hangi adımda; tamamlanınca null
provisioning_error  text null    son hata
```

Beş adımlık, dallanmasız ve paralelliksiz bir akış için ayrı bir adım tablosu fazla modeldir.
İki kolon dashboard'un ihtiyacını karşılar. Ayrı bir adım tablosu, adımlar sırasız veya
paralel çalıştığında ya da adım başına denetim izi gerektiğinde hak eder.

### 9. Provisioning adımları

| # | Adım | Yaptığı |
|---|---|---|
| 1 | `CreatingDatabase` | `CREATE DATABASE tenant_<guid-N>` |
| 2 | `MigratingSchema` | Spec 1'deki paylaşılan `TenantSchemaMigrator` sınıfı çağrılır |
| 3 | `GrantingAccess` | `st_tenant` için `GRANT` ve `ALTER DEFAULT PRIVILEGES` |
| 4 | `SeedingOwner` | Tenant veritabanında owner `TenantUser` ve rol ataması; `control_plane`'de membership |
| 5 | — | `status = Active`, `provisioning_step = null` |

Veritabanı işlemleri `st_provisioner`, control plane yazmaları `st_control` credential'ı ile
yapılır.

İkinci adımın Spec 1'deki paylaşılan sınıfı çağırması, provisioning ile migrator'ın aynı
şemayı ürettiği garantisinin somut karşılığıdır. Sistem permission'ları ve rolleri bu adımda
`HasData` üzerinden gelir; ayrı bir seed adımı yoktur.

### 10. Idempotency ve kontrol akışı

Her adım doğası gereği tekrar çalıştırılabilir:

| Adım | Neden idempotent |
|---|---|
| 1 | `pg_database` kontrolü önce yapılır |
| 2 | EF migration geçmişi uygulanmış migration'ları atlar |
| 3 | Zaten verilmiş bir `GRANT` PostgreSQL'de etkisizdir |
| 4 | `external_user_id` üzerinden insert-if-not-exists |
| 5 | Zaten `Active` ise değişiklik yapılmaz |

Bunun sonucu olarak handler'ın "kaldığım yerden devam et" mantığına ihtiyacı yoktur; her
çalıştığında adımları baştan yürütür ve tamamlanmış olanlar kendiliğinden geçilir.

Dolayısıyla `provisioning_step` kolonu **kontrol akışını yönetmez, yalnızca raporlar.**
Doğruluk idempotency'den, görünürlük kolondan gelir. Yanlış yazılabilecek bir "nerede
kalmıştım" durumu yoktur.

### 11. İki veritabanına yazan adım

Dördüncü adım iki farklı veritabanına yazar: `TenantUser` tenant veritabanına, membership
`control_plane`'e. Bunlar tek bir transaction'da olamaz; process tam aradayken ölürse biri
yazılmış diğeri yazılmamış olur.

Çözüm dağıtık transaction değil, idempotent yeniden denemedir: adım tekrar çalışır ve eksik
olanı tamamlar.

Yazma sırasının önemi yoktur, çünkü tenant o sırada hâlâ `Provisioning` durumundadır ve
middleware hiçbir isteği içeri almaz. Yarım kalmış durum dışarıya görünmez.

### 12. Başarısızlık ve kurtarma

Bir adım hata fırlatırsa handler, bulunduğu adımı ve hatayı tenant kaydına yazar ve hatayı
yukarı verir. Gerisini outbox worker'ı üstlenir: deneme sayacı, backoff, yeniden deneme.
Tenant `Provisioning` durumunda kalır ve middleware ona 503 döner.

Beş deneme sonunda mesaj ölür. Tenant `Provisioning` durumunda, hatası görünür halde bekler.

Kurtarma yolu `POST /admin/api/v1/tenants/{id}/retry-provisioning` endpoint'idir. Eski
mesajın sayacını sıfırlamaz; yeni bir outbox mesajı yazar ve başarısız olan eski mesaj kayıt
olarak kalır. Bu endpoint olmadan başarısız bir provisioning yalnızca elle veritabanı
müdahalesiyle kurtarılabilirdi.

### 13. Control plane endpoint'leri

Hepsi `/admin/api/v1/` altındadır ve Spec 1'deki `RequirePlatformAdmin` policy'sini
gerektirir.

| Endpoint | Davranış |
|---|---|
| `POST /tenants` | Gövde `{ alias }`. Tek transaction'da tenant satırı (`status=Provisioning`) ve outbox mesajı yazılır. 202 Accepted, `Location` header'ı detay endpoint'ini gösterir. |
| `GET /tenants/{id}` | `id`, `alias`, `status`, `provisioningStep`, `provisioningError`, `createdAt` |
| `GET /tenants` | `created_at` azalan sırada liste |
| `POST /tenants/{id}/retry-provisioning` | Yalnızca `status=Provisioning` iken geçerli, değilse 409. Yeni outbox mesajı yazar, 202 döner. |

Owner isteği yapan platform admin olduğu için gövdede owner alanı yoktur. Alias formatı
`TenantAlias` value object'inde doğrulanır; alias zaten alınmışsa 409 döner.

Tenant kaydında görünen ad alanı bilinçli olarak yoktur; alias hem tanımlayıcı hem görünen ad
olarak kullanılır. Gerekirse sonradan eklenecek additive bir kolondur.

### 14. Dashboard ve polling

Dashboard, `status` `Provisioning` olduğu sürece detay endpoint'ini birkaç saniyede bir
poll eder. SSE veya WebSocket kullanılmaz: on saniye süren bir işlemi izleyen bir admin
ekranı için gereksizdir.

Endpoint'ler mevcut OpenAPI → TypeScript client hattı üzerinden `packages/api-client`'a
otomatik olarak yansır.

## Bilinçli olarak yapmadıklarımız

| Yapılmayan | Hangi eşikte yapılır |
|---|---|
| Adım başına ayrı tablo ve denetim izi | Adımlar sırasız veya paralel çalıştığında |
| Reconciliation loop | `Provisioning`'de takılan tenant'lar elle kurtarılamayacak kadar sıklaştığında |
| SSE / WebSocket ile canlı ilerleme | Provisioning dakikalar sürmeye başladığında |
| Tenant listesinde sayfalama | Liste tek ekrana sığmadığında |
| Tenant veritabanlarında outbox tablosu | İlk tenant iş olayı ortaya çıktığında |
| Durable workflow motoru (Temporal vb.) | Akış saatler süren veya insan onayı içeren yapıya dönüştüğünde |

## Test stratejisi

xUnit + Testcontainers (gerçek PostgreSQL), AAA yapısı.

| Test | Kanıtladığı |
|---|---|
| Atomiklik | Tenant insert'i geri alınırsa outbox mesajı da yazılmamış olur |
| `SKIP LOCKED` | İki eşzamanlı worker aynı mesajı işlemez |
| Başarısızlık kaydı | Handler patladığında `attempt_count`, `last_error` ve `next_attempt_at` yazılı kalır |
| Ölü mesaj | `attempt_count >= 5` olan mesaj sorguya düşmez |
| Mutlu yol | Veritabanı oluşur, şema migrate edilir, owner tenant endpoint'ini çağırabilir |
| Idempotency | Handler iki kez çalıştığında sonuç aynıdır ve hata oluşmaz |
| Yarıda kalma ve kurtarma | Üçüncü adım patlatılır → tenant `Provisioning` ve hata görünür → retry → `Active` |
| Provisioning sırasında erişim | `status=Provisioning` iken tenant endpoint'i 503 döner |
| Alias çakışması | Var olan alias ile `POST` → 409 |
| Yetki | Platform admin olmayan 403 alır; bir tenant owner'ı da 403 alır |

"Yarıda kalma ve kurtarma" testi bu spec'in en değerli parçasıdır: başarısızlık hikâyesinin
tamamını uçtan uca kanıtlar.

Bunun bir tasarım sonucu vardır: **workflow adımları, bir testin içlerinden birini kasıtlı
olarak patlatabileceği şekilde ayrıştırılmalıdır.** Adımlar handler'ın gövdesine gömülü tek
bir blok değil, tek tek değiştirilebilir parçalar olmalıdır.

## Spec 3'e devredilenler

- Invitations tablosu ve davet akışı
- Mevcut bir tenant'a sonradan kullanıcı ekleme ve rol atama
- Membership'in iptali ve kullanıcı devre dışı bırakma akışları
- Admin arayüzü (`apps/admin`)
