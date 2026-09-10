# Spec 3: Tenant Üyelik Yaşam Döngüsü

**Tarih:** 2026-09-10
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api`
**Önkoşullar:** [Spec 1 — Control Plane Temeli](2026-09-09-control-plane-temeli-design.md),
[Spec 2 — Tenant Provisioning Workflow](2026-09-09-tenant-provisioning-workflow-design.md)

## Bağlam

Spec 1 control plane temelini, Spec 2 tenant'ın ve ilk sahibinin doğuşunu kurdu. Bugün bir
tenant'a yalnızca provisioning sırasında yaratılan tek sahip girebiliyor; ikinci bir kişinin
tenant'a katılmasının hiçbir yolu yok. Erişimi geri alma yolu da yok.

Bu spec o boşluğu kapatır: davet etme, kabul etme, rol atama, üyeliği geri alma ve kullanıcıyı
devre dışı bırakma.

Bu aynı zamanda projedeki ilk **gerçek tenant-scoped iş yüzeyi**. Bugüne kadar
`/{tenant-alias}/api/v1/...` altında yalnızca `whoami` vardı. Dolayısıyla bu spec iki eksiği
zorunlu olarak kapatıyor:

1. **Permission enforcement.** "Bu üye başkasını davet edebilir mi?" sorusu tam olarak bir
   permission kontrolü. Bugün middleware üyelik ve kullanıcı durumunu kontrol ediyor ama
   permission'a hiç bakmıyor; projenin karar listesindeki yetkilendirme sırasının son adımı
   eksik duruyor.
2. **Request başına tek `TenantDbContext`.** Bugün middleware kendi context'ini yaratıp hemen
   kapatıyor; tenant verisine ihtiyaç duyan bir endpoint ikinci bir bağlantı açmak zorunda
   kalırdı. İlk tenant-scoped endpoint'ler geldiği için bu şimdi çözülüyor.

## Kapsam

### Bu spec'te

- `control.invitations` tablosu, davet oluşturma, listeleme ve iptal etme
- Davet kabulü ve kabul eden kullanıcının tenant'a yerleşmesi
- Mevcut üyenin rollerinin değiştirilmesi
- Üyeliğin geri alınması, kullanıcının devre dışı bırakılması ve yeniden etkinleştirilmesi
- Üye ve rol listeleme
- Permission claim'lerinin yüklenmesi ve endpoint düzeyinde zorlanması
- Sistem kataloğuna `member` rolünün eklenmesi
- Kullanıcının kendi tenant'larını ve bekleyen davetlerini gördüğü yüzey

### Bu spec'te değil

- Admin arayüzü (`apps/admin`) — Spec 4. Farklı bir teknoloji ve farklı bir tasarım dili;
  bu spec'in ürettiği endpoint'leri tüketecek.
- Davet e-postasının gönderilmesi (aşağıda gerekçesi var)
- Tenant'a özel rol tanımlama (roller şimdilik yalnızca sistem kataloğundan gelir)
- CQRS, mediator, pipeline behavior'lar, `Result<T>`

## Kararlar

### 1. Üç route yüzeyi

| Yüzey | Yetkilendirme | Örnek |
|---|---|---|
| `/admin/api/v1/...` | Platform admin (Spec 1) | Tenant oluşturma |
| `/{tenant-alias}/api/v1/...` | Üyelik + aktif kullanıcı + permission | Davet oluşturma, üye yönetimi |
| `/api/v1/...` | Yalnızca kimlik doğrulama | Kendi tenant'larım, bekleyen davetlerim, daveti kabul et |

Üçüncü yüzey yeni. Gerekçesi zorunluluk: bir daveti kabul edecek kişinin henüz üyeliği yoktur,
dolayısıyla üyelik kapısının arkasındaki bir route'a erişemez. Kabul akışı tenant-scoped
olamaz.

`api` alias'ı `TenantAlias`'ın rezerve listesinde zaten bulunuyor, bu yüzden hiçbir tenant bu
yüzeyi gölgeleyemez.

Bu yüzey ayrıca web tarafındaki workspace switcher'ın ihtiyacı olan "hangi tenant'lara üyeyim"
sorusunun cevabını veriyor.

### 2. Invitations control plane'de yaşar ve e-posta ile anahtarlanır

Davet, bir tenant'a **giriş** hakkı verir; giriş kapısı olan membership control plane'de
durur. Davet edilen kişinin henüz bir Clerk hesabı olmayabilir, dolayısıyla kayıt
`external_user_id` ile değil **e-posta** ile anahtarlanır.

Ayrıca "bekleyen davetlerim" sorgusu tenant'lar arasıdır; her tenant veritabanını taramak
mümkün olmadığı için bu doğası gereği bir control plane sorgusudur.

```
control.invitations
  id                              uuid, PK
  tenant_id                       uuid, FK -> control.tenants
  email                           text (küçük harfe normalize edilir)
  role_code                       text
  token_hash                      text
  status                          text (Pending | Accepted | Revoked)
  invited_by_external_user_id     text
  created_at                      timestamptz
  expires_at                      timestamptz
  accepted_at                     timestamptz null
  accepted_by_external_user_id    text null
```

Aynı tenant ve e-posta için yalnızca tek bir `Pending` davet bulunabilir; bu kısmi bir unique
index ile zorlanır. Aynı e-posta farklı tenant'lara davet edilebilir.

### 3. Token'ın düz metni saklanmaz

Token, e-postayla taşınan bir bearer credential'dır. Yüksek entropili rastgele bir değer
üretilir, **düz metni yalnızca oluşturma yanıtında bir kez** döner ve veritabanında yalnızca
SHA-256 özeti saklanır. Kabul, özet üzerinden arama yapar.

Gerekçe: control plane veritabanının okunması, davetlerin ele geçirilmesi anlamına gelmemeli.
Bu, şifre sıfırlama token'larıyla ayni disiplindir.

### 4. Rol referansı `role_code` ile taşınır

Roller tenant veritabanında yaşıyor (Spec 2, Karar 1), davet ise control plane'de. Davet bu
yüzden rolü GUID ile değil `role_code` ile taşır ve kod kabul anında tenant'ın kendi
`roles` tablosuna karşı doğrulanır.

Gerekçe: control plane'de başka bir veritabanının birincil anahtarını tutmak, doğrulanamayan
ve sarkabilen bir referans yaratır. Kod stabil, okunabilir ve veritabanları arasında
taşınabilir.

### 5. Sistem kataloğuna `member` rolü eklenir

Bugün katalogda yalnızca `owner` var; herkesi owner olarak davet etmek anlamsız olurdu. Bir
davet akışının anlamlı olabilmesi için en az iki role ihtiyaç vardır.

`member` rolü `members.read` ve `roles.read` permission'larını alır. Yeni bir tenant
migration'ı ile gelir ve mevcut tenant veritabanlarına normal migration yoluyla yayılır.

### 6. Permission enforcement

Middleware, üyelik ve kullanıcı durumu kontrollerinden sonra kullanıcının permission kodlarını
tenant veritabanından okur (`user_roles` → `role_permissions` → `permissions`) ve her birini
`permission` adlı bir claim olarak ekler.

Endpoint'ler bunu ASP.NET Core'un yerel policy mekanizmasıyla zorlar:

```csharp
group.MapPost("/invitations", CreateAsync).RequirePermission("invitations.manage");
```

`RequirePermission`, `RequireAuthenticatedUser` ve `RequireClaim("permission", code)` içeren
bir policy kurar. Pipeline sırası bunu mümkün kılıyor: `UseAuthentication` →
`TenantAccessMiddleware` → `UseAuthorization`, yani claim'ler policy değerlendirilmeden önce
yerine oturuyor.

Permission'lar **cache'lenmez**. Bir rolün geri alınması anında etkili olmalıdır; bu, üyelik
kontrolüyle aynı gerekçedir. Maliyet, middleware'in zaten açtığı tenant bağlantısı üzerinde
tek bir ek sorgudur.

Ayrı bir mediator ya da pipeline behavior kurulmuyor; minimal API'de policy'nin yerel
karşılığı budur. Mediator geldiğinde `[RequiresPermission]` bu policy'nin yerini alabilir.

### 7. Request başına tek `TenantDbContext`

`TenantDbContext` scoped olarak kaydedilir ve `TenantContext`'ten çözülen veritabanı adıyla
kurulur. Middleware kendi context'ini yaratmayı bırakır, aynı scoped örneği kullanır.

Bu, tenant hangi veritabanına bağlanacağının ancak middleware çalıştıktan sonra bilindiği
gerçeğiyle uyumludur: DI çözümlemesi tembeldir ve endpoint'ler middleware'den sonra çözülür.

`TenantContext`, kimlik ve alias'ın yanına veritabanı adını da taşır.

Sonuç: bir request içinde tenant verisine dokunan herkes aynı context'i, dolayısıyla aynı
change tracker'ı ve aynı `SaveChanges` birimini paylaşır.

### 8. Davet kabulü

`POST /api/v1/invitations/accept`, gövdede `{ token }`.

Akış:
1. Token'ın SHA-256 özeti hesaplanır ve `Pending` bir davet aranır. Bulunamazsa 404.
2. Davetin süresi geçmişse 410 Gone.
3. Clerk token'ındaki `email` claim'i davetin e-postasıyla karşılaştırılır (büyük/küçük harf
   duyarsız). Eşleşmezse 403.
4. Tenant `Active` değilse 409.
5. `role_code` tenant'ın `roles` tablosunda aranır. Yoksa 409.
6. Tenant veritabanında kullanıcı yaratılır veya var olan kullanıcı yeniden `Active` yapılır;
   rol atanır. Control plane'de membership yaratılır. Davet `Accepted` olarak damgalanır.

Üçüncü adım kritik: **`email` claim'i yoksa istek reddedilir.** Token'ın ele geçirilmesi tek
başına tenant'a girmeye yetmemeli. Bu, Clerk JWT şablonunun `email` claim'ini içermesini
zorunlu kılar ve bu gereksinim `AGENTS.md`'ye yazılır.

Altıncı adımdaki yazmalar iki farklı veritabanına gidiyor ve tek transaction'da olamaz.
Provisioning'deki çözümle aynı: her adım idempotenttir, tekrar çalıştırıldığında eksik olanı
tamamlar. Yarım kalmış durum dışarıya görünmez çünkü membership olmadan tenant'a girilemez.

### 9. Üyeliğin geri alınması

`DELETE /{tenant-alias}/api/v1/members/{externalUserId}` şunları yapar:

- Control plane'deki membership kaydını siler (giriş kapısını kapatır)
- Tenant veritabanındaki kullanıcıyı `Disabled` yapar
- Kullanıcının rol atamalarını siler

Kullanıcı kaydı geçmiş için durur. Rol atamalarının silinmesi, kullanıcı ileride yeniden
etkinleştirilirse eski yetkilerin sessizce geri gelmesini engeller.

Ayrıca `POST .../members/{externalUserId}/disable` ve `/enable`, üyeliği bozmadan kullanıcıyı
geçici olarak kapatır.

### 10. Son owner korunur

`owner` rolüne sahip son kullanıcının üyeliği geri alınamaz, devre dışı bırakılamaz ve rolü
düşürülemez; bu istekler 409 döner.

Gerekçe: aksi halde tenant yönetilemez bir duruma düşer ve yalnızca veritabanına elle
müdahaleyle kurtarılabilir.

### 11. Üye listesi tenant veritabanından okunur

`GET /{tenant-alias}/api/v1/members`, tenant veritabanındaki kullanıcıları rolleri ve
durumlarıyla listeler. Control plane ile veritabanları arası bir birleştirme yapılmaz.

Bu tutarlı çalışır çünkü hem provisioning hem davet kabulü iki kaydı birlikte oluşturur ve
üyeliği geri alma kullanıcıyı `Disabled` yapar; yani "aktif kullanıcı" ile "girebilen kişi"
örtüşür.

### 12. Davet e-postası gönderilmez

Oluşturma yanıtı token'ın düz metnini bir kez döner; daveti paylaşmak daveti oluşturan
kişinin işidir.

Gerekçe: e-posta sağlayıcısı entegrasyonu bu spec'in konusu değil ve outbox deseni
provisioning ile zaten kanıtlanmış durumda. Bir e-posta sağlayıcısı geldiğinde davet
oluşturma aynı transaction'da bir outbox mesajı yazar ve bir handler e-postayı gönderir.

### 13. Süre sınırı

Davetler yedi gün sonra geçersiz olur. Bilinmeyen veya iptal edilmiş token 404, süresi geçmiş
token 410 Gone döner.

### 14. Kod yerleşimi

Tenant-scoped ve kullanıcı-scoped endpoint'ler `apps/api/src/Api` içinde özellik klasörlerine
yerleşir: `Features/Invitations`, `Features/Members`, `Features/Me`. `RequirePermission`
yardımcısı `Authorization` klasöründe durur.

Control plane'in ayrı proje olması, ileride ayrı bir deployable'a çıkarılabilmesiyle
gerekçelendirilmişti. Tenant yüzeyi API'nin kendisidir ve bir yere taşınmayacak; ayrı bir
proje ceremony olurdu.

## Bilinçli olarak yapmadıklarımız

| Yapılmayan | Hangi eşikte yapılır |
|---|---|
| Davet e-postasının gönderilmesi | Bir e-posta sağlayıcısı yapılandırıldığında |
| Tenant'a özel rol tanımlama | Bir tenant sistem rollerinin yetmediğini söylediğinde |
| Permission'ların cache'lenmesi | Tenant veritabanına giden ek sorgu ölçülebilir bir sorun olduğunda |
| Davet hatırlatma ve otomatik süre dolumu temizliği | Bekleyen davet sayısı elle yönetilemez olduğunda |
| Mediator ve `[RequiresPermission]` behavior'ı | CQRS/mediator spec'i geldiğinde; policy o zaman yerini bırakır |
| Üye listesinde sayfalama | Liste tek ekrana sığmadığında |

## Test stratejisi

xUnit + Testcontainers (gerçek PostgreSQL), AAA yapısı.

| Test | Kanıtladığı |
|---|---|
| Token özeti | Düz metin saklanmıyor, özet üzerinden arama çalışıyor |
| Davet oluşturma yetkisi | `invitations.manage` olmayan üye 403 alıyor |
| Yinelenen bekleyen davet | Aynı tenant ve e-posta için ikinci `Pending` davet reddediliyor |
| Bilinmeyen token | 404 |
| Süresi geçmiş token | 410 |
| E-posta uyuşmazlığı | 403 |
| `email` claim'i yok | 403 — token tek başına yetmiyor |
| Kabul mutlu yol | Kullanıcı, rolü ve membership'i oluşuyor; kişi tenant endpoint'ini çağırabiliyor |
| Kabul idempotency | Aynı token ikinci kez kabul edilemiyor |
| Permission claim'leri | `member` rolündeki kullanıcı `members.read` alıyor, `members.manage` almıyor |
| Üyeliğin geri alınması | Membership silindi, kullanıcı `Disabled`, rolleri kalktı, tenant endpoint'i 403 |
| Son owner koruması | Tek owner'ın üyeliği geri alınamıyor, devre dışı bırakılamıyor, rolü düşürülemiyor |
| Rol değiştirme | Roller değişiyor, permission claim'leri sonraki istekte buna göre geliyor |
| Kendi tenant'larım | Yalnızca üye olunan tenant'lar dönüyor |
| Bekleyen davetlerim | Yalnızca kendi e-postasına gelen `Pending` davetler dönüyor |

"Kabul mutlu yol" testi bu spec'in en değerli parçasıdır: daveti oluşturmaktan davet edilen
kişinin tenant endpoint'ini çağırmasına kadar tüm zinciri uçtan uca kanıtlar.

## Spec 4'e devredilenler

- `apps/admin` arayüzü: tenant listesi ve provisioning takibi, üye yönetimi, davet oluşturma
  ve paylaşma ekranları
- Web tarafındaki workspace switcher
