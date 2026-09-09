# Spec 1: Control Plane Temeli

**Tarih:** 2026-09-09
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api`

## Bağlam

Sistem multi-tenant ve izolasyon **database-per-tenant** ile sağlanıyor: tek Postgres
instance'ı, tenant başına ayrı veritabanı (`tenant_<guid-N>`). Tenant kaydı, membership ve
platform yetkileri tüm tenant'lar arasında paylaşılan bir control plane veritabanında duruyor.

Mevcut kod bu mimariye göre kurulmuş durumda ve çalışıyor, ancak üç yapısal eksiği var:

1. Veritabanı adı (`systemdb`) ve şema adı (`admin`) sektör sözlüğüyle uyuşmuyor; `admin`
   kelimesi hem şemayı hem admin portalını anlattığı için ubiquitous language belirsiz.
2. Tenant kaydında yaşam döngüsü durumu yok ve veritabanı adı domain nesnesinde GUID'den
   türetiliyor — yani domain, altyapı yerleşimini biliyor.
3. Güvenlik sınırı sadece uygulama kodunda mevcut: tek bir veritabanı kullanıcısı hem control
   plane'e hem tenant verisine, hem de DDL'e erişebiliyor. Kodda bir hata bu sınırı deler.

Bu spec bu üç eksiği kapatır ve provisioning workflow'unun (Spec 2) üzerine kurulacağı
temeli tanımlar.

Bu, projenin ölçek hedefi **öğrenme/referans mimari** olarak belirlendikten sonra yazılmıştır:
yapısal olarak doğru ve çok-pod'a hazır, ancak gelmeyecek bir yüke karşı altyapı kurmayan bir
tasarım hedeflenmiştir. Yapılmayan şeyler ve hangi eşikte yapılacakları aşağıda kayıtlıdır.

## Kapsam

### Bu spec'te

- Control plane veritabanı ve şemasının yeniden adlandırılması
- Tenant kaydının şekli, `TenantDatabaseName` value object'i, yaşam döngüsü durumu
- Platform admin kavramı ve yetkilendirmesi
- Veritabanı rolleri ve credential ayrımı
- Control plane route yüzeyinin ayrı bir projeye alınması
- Tenant çözümlemesinin Dapper'a taşınması ve cache'lenmesi
- Ayrı migrator projesi ve migration'ın deploy adımı haline gelmesi
- Mevcut migration'ların squash edilmesi
- Provisioning ile migration arasındaki sınırın tanımlanması

### Bu spec'te değil

- Outbox tablosu ve worker'ı
- Provisioning workflow'unun kendisi (adımlar, durum takibi, retry)
- Invitations
- CQRS, mediator, pipeline behavior'lar, `Result<T>`
- Serilog/Seq

Bunlar Spec 2 ve sonrasına aittir.

## Kararlar

### 1. İsimlendirme

| Şu an | Olacak |
|---|---|
| `systemdb` veritabanı | `control_plane` |
| `admin` şeması | `control` |
| `AdminDbContext` | `ControlPlaneDbContext` |
| `tenant_<guid-N>` | değişmiyor |

`control_plane`, modern SaaS/platform mühendisliği sözlüğünün baskın terimidir (control plane
vs data plane). Microsoft'un multitenant SaaS rehberindeki karşılığı "catalog database"dir,
ancak o terim dar anlamda tenant→veritabanı haritasını anlatır; buradaki kapsam membership ve
platform yetkilerini de içerdiği için daha geniş terim tercih edilmiştir.

Şema adının ayrı kalmasının sebebi grant yönetiminin okunaklılığıdır
(`GRANT USAGE ON SCHEMA control TO ...`). Adanmış bir veritabanında `public` de çalışırdı.

EF migration geçmişi tablosu `control` şemasında tutulur.

### 2. Tenant kaydı

```
Id            Guid, PK
Alias         TenantAlias, unique, değiştirilebilir
DatabaseName  TenantDatabaseName, kalıcı kolon, oluşturulduktan sonra değişmez
Status        TenantStatus
CreatedAt     timestamptz (UTC, backend üretir)
UpdatedAt     timestamptz (UTC, backend üretir)
```

`DatabaseName` artık `Id`'den hesaplanmıyor, tenant oluşturulurken bir kez üretilip kolona
yazılıyor. Üretilen değer aynı (`tenant_` + GUID'in `N` formatı), değişen tek şey gerçeğin
kaynağı.

Gerekçe: adlandırma formatı ileride değişirse, hesaplanan bir property tüm eski tenant'ları
anında yanlış veritabanına yönlendirir. Kolon olduğunda her tenant kendi adını taşımaya devam
eder. Ayrıca bir tenant yedekten farklı bir adla geri yüklendiğinde kayıt gerçeği söyler.

Format kuralları `TenantDatabaseName` value object'inde toplanır (mevcut `TenantAlias` ve
`ExternalUserId` desenine uygun):

- `ForTenant(Guid id)` → `tenant_` + `id.ToString("N")`, yeni tenant oluştururken
- `Create(string value)` → doğrulayarak kurar, veritabanından okurken
- Kurallar: küçük harf, yalnızca `[a-z0-9_]`, en fazla 63 bayt, rezerve adlarla çakışmaz
  (`postgres`, `template0`, `template1`, `control_plane`)

### 3. Tenant yaşam döngüsü

```
Provisioning → Active → Suspended → Deprovisioning → Deleted
```

Tenant middleware'i status'e göre davranır:

| Status | Yanıt | Gerekçe |
|---|---|---|
| `Active` | Servis edilir | Tek normal durum |
| `Provisioning` | 503 + `Retry-After` | Veritabanı henüz hazır değil |
| `Suspended` | 403 | Var, ancak erişim kapalı |
| `Deprovisioning`, `Deleted` | 404 | Varlığı sızdırılmaz |

Mevcut middleware yalnızca tenant'ın var olup olmadığına bakıyor; status kontrolü eklenecek.

### 4. Platform adminleri

`control.platform_admins` tablosu: `external_user_id` (Clerk `sub`, unique), `created_at`.

Kural: platform yetkisi her istekte bu tablodan sunucu tarafında doğrulanır. JWT içindeki
hiçbir claim platform yetkisi vermez — claim taşıyıcıdır, tablo otoritedir.

Platform yetkisi ile tenant rolleri birbirinden tamamen bağımsızdır. Bir tenant'ın `owner`
rolüne sahip olması hiçbir platform yetkisi doğurmaz.

`TenantAlias` rezerve listesinde `admin` zaten mevcut olduğu için control plane route yüzeyi
bir tenant tarafından kapatılamaz.

### 5. Veritabanı rolleri

| Rol | Yetki | Kullanan |
|---|---|---|
| `st_migrator` | `control_plane` ve tenant veritabanlarında DDL | Yalnızca migrator projesi |
| `st_provisioner` | `CREATEDB`, yarattığı tenant veritabanlarının sahibi | Yalnızca provisioning worker (Spec 2) |
| `st_control` | `control` şemasında DML | `/admin/...` endpoint'leri |
| `st_tenant` | Tenant veritabanlarında DML; `control` şemasında yalnızca `SELECT` (`tenants`, `memberships`) | `/{alias}/api/...` istekleri |

Sonuç: tenant trafiğini servis eden yol control plane'e yazamaz ve hiçbir yerde DDL
çalıştıramaz — uygulama kodunda hata olsa bile, çünkü credential buna izin vermez.

`st_provisioner` ayrı bir roldür çünkü `CREATE DATABASE` Postgres'te transaction bloğu içinde
çalışamaz; dolayısıyla `SECURITY DEFINER` fonksiyonu ile kısıtlama yöntemi burada
uygulanamaz. DDL yetkisini dar kapsamlı ayrı bir role vermek tek gerçekçi yoldur. Bu spec'te
yalnızca tanımlanır, kullanımı Spec 2'dedir.

**Nerede oluşturulur:** `scripts/bootstrap-roles.sql`, ortam başına bir kez, superuser ile elle
çalıştırılır. EF migration'ında **değil**, çünkü:

1. Migrator'a `CREATEROLE` vermek, migrator'ı ele geçirene veritabanının tamamını verir.
2. Şifreler ortama göre değişir; migration'lar ortamdan bağımsızdır.
3. Migration içindeki şifre, git'e girmiş bir sırdır.

Script şifreleri psql değişkeni olarak alır, dosyada sabit şifre bulunmaz.

Yeni oluşturulan tenant veritabanlarının içindeki grant'lar provisioning'in bir adımıdır
(Spec 2), çünkü bootstrap script çalıştığında o veritabanları henüz yoktur.

### 6. Uygulamada credential ayrımı

| Yol | Erişim | Credential |
|---|---|---|
| Tenant çözümleme | `control` şemasından tek düz okuma | `st_tenant` (read-only) |
| Control plane işlemleri | `ControlPlaneDbContext` (EF, yazma) | `st_control` |
| Tenant iş verisi | `TenantDbContext` (EF) | `st_tenant` |

Her yol kendi connection string'ini kullanır. Konfigürasyonda üç ayrı bağlantı tanımlanır.

### 7. Tenant çözümleme ve cache

Tenant çözümleme her istekte çalışan en sıcak yoldur. Bu yolda EF yerine **Dapper**
kullanılır: tek düz okuma, change tracking gereksiz. Bu, `AGENTS.md`'deki "Dapper'ı gerekçeli
sorgular için kullan" kuralının ilk gerçek kullanımıdır.

Sonuç `IMemoryCache`'te 60 saniye TTL ile tutulur. Cache anahtarı tenant alias'ıdır; değer
tenant kimliği, veritabanı adı ve status'ünü içerir.

Doğruluk mekanizması TTL'dir. Control plane bir tenant'ı güncellediğinde kendi process'indeki
girdiyi düşürür, ancak bu best-effort'tur: çok process'li bir kurulumda diğer process'ler en
fazla TTL süresi kadar bayat veri görebilir. Bu bilinçli olarak kabul edilmiştir; dağıtık
cache invalidation'ı bu spec'in kapsamı dışındadır.

### 8. Control plane route yüzeyi

- Rotalar `/admin/api/v1/...` altındadır.
- Yetkilendirme `RequirePlatformAdmin` policy'si ile yapılır; policy `control.platform_admins`
  tablosunu sunucu tarafında kontrol eder.
- Kod `src/ControlPlane` adında ayrı bir projede yaşar.

Ayrı projenin sınırı derleyici düzeyinde tam olarak zorlamadığı kabul edilmiştir: `Api` her iki
route ailesini de map'lediği için iki projeyi de referans alır. Kazanım, sınırın kodda görünür
olması ve ileride ayrı bir deployable'a çıkarmanın mekanik bir işe dönüşmesidir.

### 9. Migrator projesi

`src/Migrator`, konsol uygulaması, `st_migrator` credential'ı ile çalışır.

| Komut | Davranış |
|---|---|
| `migrate control-plane` | `control_plane` veritabanına control plane migration'larını uygular. Veritabanı yoksa oluşturur. |
| `migrate tenants` | `control.tenants` içindeki uygun tenant'ların **var olan** veritabanlarına tenant migration'larını uygular. |
| `migrate tenants --tenant <alias>` | Aynısını tek tenant için yapar. |

Bu komutun var olma sebebi: yeni bir tenant, provisioning sırasında zaten en güncel şemayla
doğar; daha önce oluşturulmuş tenant'lar ise bulundukları sürümde kalır. Şema her
değiştiğinde var olan tenant veritabanlarının yetiştirilmesi gerekir. İlk deploy'da tenant
listesi boş olduğu için komut hiçbir şey yapmaz — asıl işlevi ikinci ve sonraki deploy'lardadır.

Tek paylaşılan veritabanında bu sorun yoktur: bir migration çalışır ve iş biter.
Database-per-tenant'ta her şema değişikliği N veritabanına uygulanmak zorundadır; bu komut o
bedelin karşılığıdır.

Tenant seçimi status'e göredir:

| Status | Davranış |
|---|---|
| `Active`, `Suspended` | Migrate edilir. Veritabanı yoksa **hata**. |
| `Provisioning`, `Deprovisioning`, `Deleted` | Atlanır, raporda "skipped" görünür. Hata değildir. |

`Active` bir tenant'ın veritabanı yoksa boş veritabanı **oluşturulmaz**: bu durum veri kaybı
anlamına gelir ve boş bir veritabanı yaratmak sorunu çözmez, gizler.

Hata davranışı: tek bir tenant'ın başarısızlığı çalışmayı durdurmaz, kalan tenant'lara devam
edilir ve tümü raporlanır. Herhangi bir başarısızlık varsa süreç sıfırdan farklı exit code ile
biter, böylece deploy bloke olur. Control plane'e hiç bağlanamamak gibi sistemik hatalarda ilk
anda durulur.

Migrator'ın yapmadıkları: tenant veritabanı oluşturmaz, rol oluşturmaz, iş verisi seed etmez.

### 10. Migration squash

Mevcut control plane migration'ları (`InitialAdmin`, `AddMemberships`, `AddRoles`,
`SimplifyAdminMemberships`) ve tenant migration'ı silinip her context için tek bir başlangıç
migration'ı olarak yeniden üretilir.

Gerekçe: "migration'lar yeniden yazılmaz" kuralı, gerçek bir ortamda çalışmış migration'lar
için geçerlidir. Bu migration'lar hiçbir kalıcı ortamda çalışmadı ve terk edilmiş bir tasarıma
(schema-per-tenant, `admin` şeması) referans veriyorlar.

**Bu dönem henüz bitmedi ve squash onun tek örneği değil.** Bir migration, düşürülemeyecek
herhangi bir yerde (staging, prod, paylaşılan dev veritabanı, başka bir geliştiricinin
makinesi) çalışana kadar, var olan bir migration'ı düzenleyip lokal veritabanını yeniden
kurmak doğru ve en ucuz yoldur. Bu dönemde bir yeniden adlandırma için ayrı migration yazmak
gereksiz törendir.

O eşik aşıldığı anda **iki kural birlikte** yürürlüğe girer:

1. **İleriye doğru migration:** var olan migration'lar bir daha düzenlenmez; yeniden
   adlandırma ve yazım hatası dahil her değişiklik yeni bir migration ile yapılır.
2. **Expand/contract:** bir migration, o an yayında olan kodu kırmamalıdır. Tablo veya
   nullable kolon eklemek güvenlidir; silmek, yeniden adlandırmak ve tip değiştirmek bir
   sonraki sürüme bırakılır — önce yeni kolon eklenir, bir süre ikisine birden yazılır, eskisi
   sonra silinir.

İkisi aynı anda başlar çünkü sebepleri aynıdır: artık sizin kontrolünüzde olmayan bir yerde
çalışmış bir şema vardır.

Eşik aşıldığında `apps/api/AGENTS.md`'ye eklenecek satır: "Migrations must not break the
currently deployed code: add before removing, and defer destructive changes to a later
release." Bu satır o güne kadar eklenmez, çünkü erken eklenirse her şema değişikliğini
gereksiz yere iki sürüme yayar.

### 11. Provisioning ile migration sınırı

| | Yeni tenant | Var olan tenant'lar |
|---|---|---|
| Tetikleyici | Admin tenant oluşturur | Deploy pipeline |
| Çalıştıran | Provisioning worker (runtime) | Migrator projesi |
| Veritabanı yoksa | Oluşturur | Hata verir |
| Kapsam | Tek tenant | Tüm `Active`/`Suspended` |
| Credential | `st_provisioner` | `st_migrator` |

"Verilen bir veritabanına tenant migration'larını uygula" işi Infrastructure'da tek bir
paylaşılan sınıfta yaşar; hem migrator hem provisioning worker onu çağırır. Migration mantığı
iki yerde yazılmaz.

`migrate tenants` yeni tenant için çalışmaz.

### 12. Seed verisi ayrımı

Kural: **tüm tenant'larda aynı olan şey migration'a, o tenant'a özel olan şey provisioning'e.**

- **Migration (EF `HasData`):** sistem permission'ları ve rolleri. Bunlar
  `Domain/Access/SystemAccessCatalog.cs` içinde sabit GUID'lerle tanımlıdır. `HasData`
  konfigürasyonu bu katalogdan okur, listeyi kopyalamaz.
- **Provisioning (Spec 2):** owner kullanıcısının kaydı ve rol ataması — tenant'ı kimin
  oluşturduğuna bağlı çalışma zamanı değeri.

Bu ayrımın somut kazancı: ileride yeni bir permission eklendiğinde `migrate tenants` onu var
olan tüm tenant veritabanlarına taşır; ayrı bir backfill mekanizması gerekmez.

### 13. Deploy sırası

```
1. scripts/bootstrap-roles.sql       (ortam kurulurken bir kez, elle, superuser)
2. migrator: migrate control-plane
3. migrator: migrate tenants
4. API deploy
```

2 ve 3 başarılı olmadan 4 çalışmaz. Şimdilik `apps/api/AGENTS.md`'de belgelenmiş komutlardır;
CI kurulduğunda aynı sıra otomatikleşir, K8s'te migrator bir Job'a dönüşür.

Migration'lar API'den önce çalıştığı için kısa bir süre eski API kodu yeni şemaya karşı çalışır.
Bu nedenle **expand/contract** disiplini uygulanır: additive değişiklik önce, kod sonra, yıkıcı
değişiklik bir sonraki sürümde. Bu kuralın ne zaman yürürlüğe girdiği Karar 10'da tanımlıdır.

## Bilinçli olarak yapmadıklarımız

| Yapılmayan | Hangi eşikte yapılır |
|---|---|
| Tenant kaydında `shard_id` / `region` | İkinci bir Postgres instance'ı veya bölge girdiğinde. `DatabaseName` zaten veri olduğu için sonradan eklemek bir nullable kolon ve bir config lookup'tır. |
| PgBouncer | Pod sayısı × tenant sayısı × pool size bağlantı matematiği sorun olduğunda. K8s'e geçiş bunu tenant sayısından önce tetikler. |
| Dağıtık cache (Redis/HybridCache) | 60 saniyelik bayatlık gerçek bir soruna dönüştüğünde. |
| Paralel migration fan-out | Sıralı migration deploy penceresine sığmadığında. |
| Ayrı deployable control plane servisi | Güvenlik sınırının process düzeyinde olması gerektiğinde. |
| Temporal / durable workflow motoru | Workflow'lar saatler süren veya insan onayı içeren yapıya dönüştüğünde. |

## Test stratejisi

xUnit + Testcontainers (gerçek PostgreSQL), AAA yapısı.

| Test | Proje | Kanıtladığı |
|---|---|---|
| `TenantDatabaseName` kuralları | UnitTests | Format, uzunluk sınırı, rezerve ad çakışması |
| `TenantStatus` → HTTP eşleşmesi | IntegrationTests | Her status için doğru yanıt (503/403/404) |
| Tenant çözümleme | IntegrationTests | Doğru tenant çözülüyor, cache davranışı |
| Platform admin policy | IntegrationTests | Tenant owner'ın platform yetkisi yok; platform admin geçiyor |
| Credential sınırı | TenantIsolationTests | `st_tenant` ile control plane'e yazma ve DDL başarısız oluyor |
| Migrator status filtresi | IntegrationTests | `Provisioning` atlanıyor, `Active` + eksik veritabanı hata veriyor |
| Şema yakınsaması | IntegrationTests | Sıfırdan kurulan veritabanı ile eski sürümden yükseltilen veritabanının `__EFMigrationsHistory` içeriği aynı |
| Tenant veri izolasyonu | TenantIsolationTests | Bir tenant'ın verisi diğerinden okunamıyor (mevcut test taşınır) |

`TenantIsolationTests`, `AGENTS.md`'nin gerektirdiği gibi ayrı ve adlandırılmış bir proje olarak
oluşturulur; mevcut izolasyon testleri `IntegrationTests`'ten oraya taşınır.

Credential sınırı testi bu spec'in en değerli test parçasıdır: güvenlik sınırını tasarım
iddiası olarak bırakmak yerine, Testcontainers içinde rolleri bootstrap script'i ile kurup
`st_tenant` bağlantısıyla yazma ve DDL denemelerinin başarısız olduğunu kanıtlar.

## Spec 2'ye devredilenler

- Outbox tablosu ve `SELECT ... FOR UPDATE SKIP LOCKED` worker'ı
- Provisioning state machine, adım takibi, idempotency ve retry
- Yeni tenant veritabanında `GRANT` adımı (`ALTER DEFAULT PRIVILEGES`)
- Owner kullanıcısının oluşturulması ve rol ataması
- `st_provisioner` rolünün kullanımı
- Invitations tablosu
- Membership'in `role`/`status` alanları ve rollerin tenant veritabanında yaşamasıyla ortaya
  çıkan tutarsızlığın çözümü
- Provisioning ilerlemesini gösteren dashboard durum endpoint'i
