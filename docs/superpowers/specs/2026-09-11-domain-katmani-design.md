# Spec 6: Domain Katmanı Yeniden Yapılandırması

**Tarih:** 2026-09-11
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api` — `src/Domain` başta olmak üzere ona dokunan her katman
**Önkoşullar:** [Spec 1 — Control Plane Temeli](2026-09-09-control-plane-temeli-design.md),
[Spec 2 — Tenant Provisioning Workflow](2026-09-09-tenant-provisioning-workflow-design.md),
[Spec 3 — Tenant Üyelik Yaşam Döngüsü](2026-09-10-tenant-membership-lifecycle-design.md),
[Spec 4 — Application Katmanı](2026-09-10-application-katmani-design.md)

> Spec 5 `apps/admin` arayüzü için ayrılmıştı ve henüz yazılmadı. Bu numara backend'in
> domain katmanına verildi; admin arayüzü kendi numarasını korur.

## Bağlam

Dört spec boyunca domain katmanı hep yan üründü. Her spec kendi işini yapmak için oraya
bir iki tip ekledi, kimse bütününe bakmadı. Sonuç, tek tek bakıldığında savunulabilir
ama yan yana konduğunda tutarsız bir katman:

- `Domain/Access/` klasörü iki ayrı veritabanının varlıklarını bir arada tutuyor.
  Bu projenin merkezî kuralı tenant izolasyonuysa, klasör düzeni onu gizliyor.
- `ExternalUserId` elle yazılmış bir sınıf ve `==` operatörünü ezmiyor; diğer bütün
  değer nesneleri `record`. Bugün her karşılaştırma EF ifade ağacında olduğu için
  SQL'e çevriliyor ve sorun çıkmıyor. Bellekte yapılacak ilk karşılaştırma sessizce
  referans eşitliğine düşer.
- `Tenant.ChangeStatus` her geçişe izin veriyor. Karar 31 bir yaşam döngüsü tanımlıyor,
  aggregate onu zorlamıyor. `Deleted` bir tenant tekrar `Active` yapılabilir.
- `TenantUser`, `TenantRole`, `TenantPermission` isimlerindeki `Tenant` öneki hiçbir
  bilgi taşımıyor; tenant veritabanındaki her satır zaten tenant'a ait.
- `TenantUserRole` ve `TenantRolePermission` kimliği olmayan ilişki satırları ama domain
  tipi olarak duruyorlar; karşılığında `User` kendi rollerini taşıyamıyor ve üç handler
  ilişki satırlarını elle yönetiyor.
- `OwnerRoster` kuralı biliyor ama kararı vermiyor. Sorguyu ve kararı yine handler
  yapıyor, yani kural üç ayrı yerde hatırlanmak zorunda.
- `Membership` ve `TenantUser` hiçbir zaman damgası taşımıyor; `Tenant` ve `Invitation`
  taşıyor. Karar 22 damgaların backend'de üretilmesini söylüyor, kimin taşıyacağını
  söylemiyor.
- `ListMembers` üye listesini yalnızca `ExternalUserId` ile dönüyor. Ekranda
  `user_2abc...` görünüyor; bu endpoint bugünkü haliyle kullanılamaz.
- Davet hem token hem doğrulanmış e-posta eşleşmesi istiyor. İkisi aynı şeyi kanıtlıyor.

Bu spec katmanı bütün olarak elden geçirir ve bunu yaparken davet akışının da bir
yanlış varsayımını düzeltir.

## Kapsam

### Bu spec'te

- Klasör düzeni ve isimlendirme
- `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent` ve event dağıtımı
- Tipli kimlikler
- Değer nesnelerinin tutarlı hale gelmesi
- `Tenant` statü geçişlerinin kapanması
- İlişki sınıflarının domainden çıkması, `User.Roles` navigasyonu
- `Role` ve `Permission`'ın katalog olarak netleşmesi
- Davetten token'ın kalkması, kararın doğrulanmış e-posta claim'ine bağlanması
- Clerk'in restricted sign-up modu ile birlikte çalışan davet akışı
- `User` satırının e-posta taşıması
- Son owner korumasının korkuluğa inmesi
- Denetim damgalarının EF interceptor'üne taşınması
- Migration'ların sıfırlanması
- Değişen uçlar için OpenAPI şemasının ve TypeScript istemcisinin yeniden üretilmesi

### Bu spec'te değil

- **Somut domain event'ler.** Mekanizma yazılır, tek bir gerçek event ile kanıtlanır.
  İkinci event'in kendi gerekçesi olur.
- **Tenant'a özel rol tanımlama.** Model buna hazırlanır (her rolün kodu olur) ama
  özellik yazılmaz.
- **Alan adı doğrulama, otomatik katılma, SCIM.** Davetin yerine geçmezler, yanına
  gelirler; ikisi de kendi spec'ini hak eder.
- **Concurrency kontrolü (karar 14) ve dual ID (karar 15).** Tetikleyicileri dolmadı.
- **`apps/admin` arayüzü.**

## Kararlar

### 1. Klasör düzeni veritabanı sahipliğini gösterir

Bu sistemde izolasyon sınırı fiziksel bir dağıtım detayı değil, merkezî invariant.
Klasör düzeni onu görünür yapar: bir tipe yanlış taraftan uzandığında `using` satırından
anlaşılır.

```
Domain/
  Shared/
    Entity.cs                Entity<TId>, AggregateRoot<TId>, IDomainEvent
    EmailAddress.cs
    ExternalUserId.cs
  ControlPlane/                          -> control_plane veritabanı
    Tenants/
      Tenant.cs              TenantId ile birlikte
      TenantAlias.cs
      TenantDatabaseName.cs
      TenantStatus.cs
      TenantProvisioningStep.cs
    Memberships/
      Membership.cs          MembershipId ile birlikte
    Invitations/
      Invitation.cs          InvitationId ve InvitationStatus ile birlikte
      InvitationAcceptance.cs
      InvitationCreated.cs
    Administration/
      PlatformAdmin.cs       PlatformAdminId ile birlikte
  Authorization/                         -> tenant veritabanı
    User.cs                  UserId ve UserStatus ile birlikte
    Role.cs                  RoleId ile birlikte
    Permission.cs            PermissionId ile birlikte
    AccessCatalog.cs
```

Değer nesneleri tipe göre ayrı bir klasörde toplanmaz. `TenantAlias`, `Tenant`'ın
yanında durur. Tipe göre klasörleme kavramı parçalar. Yalnızca iki bağlamda birden geçen
`EmailAddress` ve `ExternalUserId` `Shared` altındadır.

Tipli kimlikler kendi dosyalarını almaz, ait oldukları entity'nin dosyasında tanımlanır.

`Authorization` klasörünün tenant veritabanına ait olduğu isimden anlaşılmaz; bunu
klasördeki tek satırlık bir not söyler.

### 2. `Workspace` değil `tenant`, ve `Tenant` öneki düşer

"Workspace" müşterinin ürün diline ait bir kelime ve ortada henüz bir ürün yüzeyi yok.
Bu proje bugün tenant kayıt defteri ve erişim yönetiminden ibaret; o alanın dili
"tenant". Rota `/{tenant-alias}/`, veritabanı `tenant_<guid>`, testler
`TenantIsolationTests`. İkinci bir kelime sokmak kavramsal bütünlüğü bozar. İleride
arayüzde "workspace" yazmak bir metin kararıdır, domain yeniden adlandırması değil.

Bunun yan faydası, altyapıda hiçbir yeniden adlandırma gerekmemesidir. `TenantDbContext`
ve `TenantContext` olduğu gibi kalır.

Buna karşılık tip isimlerindeki `Tenant` öneki düşer, çünkü namespace zaten söylüyor.

| Bugün | Yarın |
|---|---|
| `Access.Users.TenantUser` | `Authorization.User` |
| `Access.Users.TenantUserStatus` | `Authorization.UserStatus` |
| `Access.Roles.TenantRole` | `Authorization.Role` |
| `Access.Permissions.TenantPermission` | `Authorization.Permission` |
| `Access.SystemAccessCatalog` | `Authorization.AccessCatalog` |
| `Access.Membership` | `ControlPlane.Memberships.Membership` |
| `Access.Invitation` | `ControlPlane.Invitations.Invitation` |
| `Access.PlatformAdmin` | `ControlPlane.Administration.PlatformAdmin` |
| `Access.EmailAddress`, `Access.ExternalUserId` | `Shared.*` |
| `Tenants.*` | `ControlPlane.Tenants.*` |
| `Access.Users.TenantUserRole` | siliniyor |
| `Access.Roles.TenantRolePermission` | siliniyor |
| `Access.Users.OwnerRoster` | siliniyor |

### 3. `Entity<TId>` ve `AggregateRoot<TId>` yazılır, `ValueObject` yazılmaz

```csharp
public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && other.Id.Equals(Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
```

`==` operatörü de yazılır. Yazılmazsa bugün `ExternalUserId`'de bulunan tuzağın aynısı
entity tarafında oluşur: referans eşitliğine sessizce düşen bir karşılaştırma.

`ValueObject` base class'ı **yazılmaz**. C# `record` yapısal eşitliği zaten veriyor; eski
`GetEqualityComponents()` deseni dilin bu özelliğinden önceki dünyaya ait.

İki taban sınıfın ayrımı bugünkü listeyi bölmez, çünkü bugün her entity aynı zamanda
root. Ayrımın anlamı ileriye dönüktür: `AggregateRoot`, handler'ın yükleyip kaydettiği ve
event yayınlayabilen şeydir. Bir root'un altında kendi başına yaşamayan bir alt entity
çıktığında o `Entity<TId>` kalır ve event fırlatamaz.

`Role` ve `Permission` ikisinden de türemez (karar 9).

### 4. Domain event mekanizması yazılır, tek gerçek event ile kanıtlanır

Karar 16 bu işi "ilk domain event ihtiyacı" tetikleyicisine bağlamıştı. Spec 4 o
tetikleyiciyi daha da daraltmıştı: "Davet e-postası. `InvitationCreated` ilk gerçek
integration event olur." Karar 11 ile Clerk'e daveti outbox üzerinden göndermeye karar
verildiğinde o tetikleyici çalıştı.

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct
{
    private readonly List<IDomainEvent> events = [];
    public IReadOnlyCollection<IDomainEvent> Events => events;
    protected void Raise(IDomainEvent domainEvent) => events.Add(domainEvent);
    public void ClearEvents() => events.Clear();
}
```

Dağıtım da aynı anda yazılır. `Raise` var ama dağıtan yoksa event listeye girer, kimse
okumaz ve sessizce kaybolur. Zor olan kısım taban sınıf değil, işlem sınırıdır.

Dağıtım `UnitOfWorkBehavior` içinde, `SaveChanges` çağrılmadan **önce** yapılır. Change
tracker'daki root'lardan eventler toplanır, listeleri temizlenir ve her biri kendi
`IDomainEventHandler<TEvent>` implementasyonlarına verilir. Handler'ların ürettiği yeni
eventler liste boşalana kadar aynı döngüde işlenir. Sonra tek bir `SaveChanges` çalışır.
Dispatch, mevcut elle yazılmış mediator'ün kayıt listesini kullanır (spec 4, karar 6).

**Kural tek cümledir: domain event handler'ı yalnızca aynı `DbContext`'e yazabilir.**
Süreç dışına çıkan her şey outbox'tan geçer (karar 24). Böylece event mekanizması ile
outbox birbirinin yerine geçmez, zincir olur:

```
Invitation.Create -> InvitationCreated -> handler outbox satırını yazar
                                       -> outbox işçisi Clerk'i çağırır
```

Bugün yazılan tek somut event `InvitationCreated`'dır. Provisioning'in outbox satırı elle
yazılmaya devam eder; onu eventlere taşımak bu spec'in işi değildir.

### 5. Tipli kimlikler

```csharp
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());
    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant ID cannot be empty.", nameof(value))
        : new TenantId(value);
}
```

Gerekçe, kimliklerin yer değiştirmesinin bugün derleme hatası vermemesidir.
`Membership.Create(id, tenantId, externalUserId)` çağrısında ilk iki argüman yer
değiştirse kod derlenir. Bu tür bir kararı sonradan almak her imzaya dokunmayı gerektirir.

İki yan faydası var. Factory metotlarının başındaki `if (id == Guid.Empty) throw`
satırlarının hepsi silinir; kontrol tek yere, `From`a iner. `Guid.CreateVersion7()`
çağrıları handler'lardan `New`e toplanır.

Sınırı açıkça kaydedilir: C# her struct için `default` değerine izin verir, yani
`default(TenantId)` her zaman üretilebilir ve boş `Guid` taşır. Bunu tip sistemiyle
kapatmanın yolu yoktur. `From` yalnızca dışarıdan gelen veriyi süzer.

Kimlikler API sözleşmesine çıkmaz. Endpoint'ler ve DTO'lar `Guid` kalır, üretilen OpenAPI
şeması bu karardan etkilenmez. EF tarafında dönüştürücüler `ConfigureConventions` ile tek
yerden tanımlanır.

### 6. Değer nesneleri `record` olur

`ExternalUserId` elle yazılmış sınıflıktan `sealed record`a geçer. `EmailAddress.TryCreate`
başarısızlıkta `null!` vermeyi bırakır, `[MaybeNullWhen(false)]` kullanır. `TenantAlias`
ve `TenantDatabaseName` olduğu gibi kalır.

### 7. `Tenant` statü geçişleri kapanır

`ChangeStatus` silinir; yerine kaynak durumu kontrol eden isimli metotlar gelir.

| Metot | İzin verilen geçiş |
|---|---|
| `CompleteProvisioning` | Provisioning -> Active |
| `Suspend` | Active -> Suspended |
| `Resume` | Suspended -> Active |
| `BeginDeprovisioning` | Active veya Suspended -> Deprovisioning |
| `MarkDeleted` | Deprovisioning -> Deleted |

Kural dışı çağrı `InvalidOperationException` atar. Bu bir kullanıcı hatası değil,
programlama hatasıdır; `Result` ile temsil edilmez.

`ChangeStatus` üretim kodunda hiç kullanılmıyor, yalnızca testlerde geçiyor. Yani bu
değişikliğin maliyeti testlerin okunaklılaşmasından ibarettir.

### 8. İlişki sınıfları domainden çıkar, `User` rollerini kendi taşır

```csharp
public sealed class User : AggregateRoot<UserId>
{
    private readonly List<Role> roles = [];
    public IReadOnlyCollection<Role> Roles => roles;

    public void AssignRoles(IEnumerable<Role> replacement)
    {
        roles.Clear();
        roles.AddRange(replacement);
    }
}
```

`TenantUserRole` ve `TenantRolePermission` domainden çıkar; EF ikisini de skip navigation
olarak kurar. Tablo adları (`user_roles`, `role_permissions`), kolon adları ve tohum
verisi aynı kalır.

Kazanç somuttur. Middleware'deki elle yazılmış iki `Join` tek ifadeye iner:

```csharp
user.Roles.SelectMany(role => role.Permissions).Select(permission => permission.Code).Distinct()
```

`ReplaceMemberRoles` handler'ı ilişki satırlarını tek tek silip eklemeyi bırakır,
`user.AssignRoles(roles)` çağırır.

Bedeli tek bir yerdedir: `role_permissions` tohum verisi açık bir sınıf üzerinden değil,
EF'in sözlük tabanlı join eşlemesi üzerinden yazılır.

### 9. `Role` ve `Permission` katalogdur; her rolün kodu olur

Bu iki tipin bugün factory'si, mutasyonu, davranışı yoktur. Gerçek model
`AccessCatalog`'tur; tablolar onun izdüşümüdür ve yalnızca SQL'in join yapabilmesi için
vardır. Bu yüzden `Entity<TId>`'den türemezler: kimlikleri vardır, hayatları yoktur.

Bir tenant kendi rolünü tanımlayabildiği gün rolün bir hayatı olur (oluşturulur, adı
değişir, permission kümesi değişir, silinir) ve o gün gerçek bir entity'ye dönüşür.
Veritabanı aynı kalır; değişen şey sınıfın davranış kazanmasıdır.

`Role.Code` bugün nullable; **zorunlu hale gelir**. Bu bir kısıtlama değil, bir
taahhüttür: sistem rolleri sabit kodlarını alır, tenant'ın tanımladığı rol de adından
türeyen ve o tenant içinde tekil olan bir kod alır. Böylece davet her role işaret
edebilir (karar 10) ve bugünkü nullable'ın sebebi ortadan kalkar.

`AccessCatalog` owner ve member rollerini isimli özellik olarak verir. Provisioning
handler'ındaki `"owner"` düz metni ve application tarafındaki `OwnerRoleCode` sabiti
birlikte silinir.

### 10. Davetten token kalkar, kararı doğrulanmış e-posta claim'i verir

Bugünkü kabul hem token hem e-posta eşleşmesi ister. İkisi aynı şeyi kanıtlıyor: posta
kutusuna erişim. İki senaryo bunu gösterir:

- **Token çalınırsa** saldırgan yine giremez, çünkü e-postası eşleşmez.
- **Token kaybolursa** davetli yine girebilir, çünkü daveti `/api/v1/me/invitations`
  listesinde e-postasına göre zaten görür. O kod bugün de var.

Yani token güvenlik katmanı değil, bir kısayoldur. Kaldırılır. `token_hash` kolonu,
`InvitationTokens` sınıfı ve 404 ile 410 arasındaki token'a özgü ayrım silinir; süresi
geçmiş davetin 410 dönmesi korunur.

Kabul `/api/v1/me/invitations/{id}/accept` ile yapılır. **Bu kimliğin gizli olması
gerekmez**, çünkü kararı e-posta eşleşmesi verir; başkasının davetinin kimliğini bilmek
hiçbir işe yaramaz.

Bunun şartı, e-posta claim'ine ancak **doğrulanmışsa** güvenilmesidir. OIDC dünyasında
doğrulanmamış `email` claim'ini kabul etmek bilinen bir açıktır; kullanıcı kendi
e-postasını yazabildiğinde başkasının davetini alabilir. Kural "e-posta eşleşiyor mu"
değil, "e-posta doğrulanmış **ve** eşleşiyor mu" olur. Doğrulanmamışsa sessiz boş liste
değil, açık bir hata döner.

Bu, karar 36'yı (token'ın SHA-256 özeti) geri alır.

Rol referansı `role_code` olarak kalır ve **çoğullaşır** (`role_codes`). Opak rol
kimliği saklamak merkezî tarafı sadeleştirirdi ama `ListMyInvitations` davetin rolünü
ekranda gösteriyor ve o endpoint kullanıcı yüzeyinde, açık bir tenant veritabanı
bağlantısı olmadan çalışıyor. Opak kimlik, her davet başına ayrı bir tenant bağlantısı
gerektirirdi. Çoğullaşmasının sebebi ise üye yönetiminin (`ReplaceMemberRoles`) zaten
roller listesiyle çalışması; tek rolle sınırlı davet gereksiz bir asimetridir.

Süre sınırı korunur (7 gün). Token kalktığı için artık bir güvenlik önlemi değil, hijyen
önlemidir: işten ayrılmış birinin daveti sonsuza kadar açık kalmasın.

Kabul edilen davet **silinmez**, `Accepted` olarak durur. Böylece "bu kişiyi kim, ne
zaman davet etti" sorusunun cevabı kalır.

### 11. Clerk restricted sign-up ile birlikte çalışılır, davet outbox üzerinden gider

Clerk restricted moddadır: davet edilmeyen kişi hesap açamaz. Bu açık bırakılmaz, çünkü
kendi başına hesap açan birinin yapabileceği tek şey boş bir ekrana bakmaktır; karşılığında
bot kaydına ve spam'e açık bir yüzey doğar. Bunu değiştirecek tek durum self-serve tenant
kaydıdır; bugün tenant yaratma yalnızca platform admin yüzeyinden yapılır.

İki ayrı kapı olduğu net tutulur:

- **Hesap açma izni** Clerk'tedir.
- **Tenant'a girme izni** bizdedir. Clerk'in tenant diye bir kavramı yoktur.

Davet oluşturulduğunda `InvitationCreated` fırlar, handler'ı outbox satırını aynı
transaction'da yazar, outbox işçisi Clerk'e "bu adrese hesap açma izni ver" der. Clerk
davet e-postasını kendi gönderir.

Doğrudan çağrı yapılmamasının sebebi, adımın tekrar edilebilir olmasıdır: Clerk o an
erişilemezse davet kaybolmaz. Bu, provisioning'de kullanılan desenin aynısıdır
(karar 24 ve 35).

**Clerk'in daveti yetki vermez, yalnızca hesap açma iznini verir.** Tenant'a girme kararı
bizim kabul adımımızda, doğrulanmış e-posta eşleşmesiyle verilir. Webhook kullanılmaz ve
hiçbir yetki Clerk'ten gelen bir mesaja bağlanmaz. Böylece IdP'ye olan bağ tek bir outbox
handler'ına iner; IdP değişirse o handler değişir, davet mantığı ve kabul akışı
dokunulmadan kalır.

Clerk erişimi somut bir sınıf olarak yazılır, arayüz arkasına saklanmaz. Olmayan bir
sağlayıcı değişimi için soyutlama yazmak boş soyutlamadır; koruma tek çağrı yerinden gelir.

İki dürüst pürüz kaydedilir:

- Davetlinin Clerk hesabı zaten varsa (başka bir tenant'ın üyesiyse) hesap açma izni
  vermenin anlamı yoktur; o adım atlanır. Yani "bu e-postanın hesabı var mı" kontrolü
  gerekir.
- Davet iptal edilirse Clerk'teki davetin de iptal edilmesi gerekir. Bu da outbox
  üzerinden gider.

Davet e-postasının bizim tarafımızdan gönderilmesi gerekmez; bu, "e-posta gönderimi
ertelendi" maddesini listeden düşürür.

### 12. Kabul bilgisi tek bir değer nesnesine iner

```csharp
public sealed record InvitationAcceptance(ExternalUserId By, DateTimeOffset At);
```

Bugün `AcceptedAt` ve `AcceptedByExternalUserId` ayrı ayrı nullable, yani "kabul edilmiş
ama kim ettiği bilinmiyor" gibi anlamsız bir durum tip olarak mümkün. Tek nullable değere
inince o durum inşa edilemez hale gelir. `Revoke` içindeki ölü `AcceptedAt = null`
ataması da kendiliğinden kalkar. Veritabanında aynı iki kolon, owned type olarak eşlenir.

### 13. Tenant'taki `User` e-posta taşır

`ListMembers` bugün yalnızca `ExternalUserId` dönüyor, yani üye listesi opak kimliklerden
oluşuyor ve kullanılamıyor. E-posta kolonu bu yüzden gereklidir; spekülatif bir sigorta
değildir.

Yan faydası, IdP değişimi senaryosunda kullanıcıların e-posta üzerinden yeniden
eşlenebilmesidir. Bugün tenant'taki kullanıcı satırı Clerk'in verdiği subject değerine
bağlıdır ve IdP değişirse o değerlerin hepsi geçersiz olur.

İsim alanı eklenmez; gerçekten bayatlayan bir kopya olur ve kimliği e-posta zaten verir.
Tetikleyicisi, üye listesinde insan adı göstermenin gerekli hale gelmesidir.

### 14. Son owner koruması korkuluğa iner, domainden çıkar

Kural şudur: bir tenant'ın her zaman en az bir aktif owner'ı kalmalı. Aksi halde tenant
yönetilemez hale gelir. Üç yoldan delinebilir: son owner'ın üyeliğinin iptali, devre dışı
bırakılması, rolünün düşürülmesi.

Bu bir güvenlik invariant'ı değil, kullanıcıyı kendinden koruyan bir korkuluktur. GitHub
ve Slack da aynısını yapar. `OwnerRoster` silinir; yerine `Application/Features/Members/` altında tek bir yardımcı
gelir: hedef kullanıcı çıkarıldığında geriye aktif owner kalıyor mu. `RevokeMember`,
`DisableMember` ve `ReplaceMemberRoles` onu çağırır.

Bu, **Spec 4'ün 11. kararını geri alır.** O karar kuralı "projedeki ilk gerçek domain
kuralı" diyerek domaine taşımıştı. Geri alma gerekçesi: kural bir invariant değil, ve
nesne onu zorlayamıyordu çünkü kararı yine handler veriyordu. Nesne sığ bir sarmalayıcıydı.

Kuralı atlanamaz kılmanın iki yolu tartışıldı ve ikisi de reddedildi:

- **Roster aggregate'i.** Tenant'ın bütün kullanıcı ve rol atamalarını tek aggregate
  yapmak kuralı atlanamaz kılardı ama tek kişinin rolünü değiştirmek için tenant'ın bütün
  üyelerini yüklemeyi gerektirirdi.
- **Veritabanı seviyesinde trigger.** En güçlü garanti, ama kural domain dilinden çıkar,
  temiz hata mesajı üretmek ve birim testi yazmak zorlaşır.

İkisinin de tetikleyicisi aynıdır: korkuluğun atlandığı ilk gerçek olay.

Ayrıca kilitlenmiş bir tenant'ı kurtarmak için **servis hesabı açılmaz.** Her tenant'ın
veritabanında bizim elimizde duran silinemez bir owner hesabı, saklanması gereken bir
parola ve bütün tenant'ları birden düşürebilecek bir ana anahtar demektir. Alias'tan
türeyen e-posta tahmin edilebilir olur, karar 3 (Clerk yalnızca kimlik doğrulama, biz
kimlik bilgisi tutmayız) ve karar 34 (platform yetkisi tenant rolü doğurmaz) çiğnenir.
Kurtarmanın doğru yolu platform admin yüzeyinden yapılan, denetlenebilir bir "bu tenant'a
owner ata" işlemidir; bugün admin yüzeyinin tenant veritabanına yazma yetkisi olmadığı
için (karar 32) o iş de outbox üzerinden gider. Tetikleyicisi ilk gerçek kilitlenmedir.

### 15. Denetim damgaları EF interceptor'ünde yaşar

Domain'de yalnızca boş bir `IAuditable` işaretleyicisi bulunur; sadece okunabilir
`CreatedAt` ve `UpdatedAt` tanımlar. Damgayı EF interceptor'ü `TimeProvider` üzerinden,
EF metadata'sı aracılığıyla yazar. Böylece hiçbir entity'de set edilebilir zaman alanı
olmaz ve hiçbir handler damga yazmayı hatırlamak zorunda kalmaz.

İşareti taşıyanlar: `Tenant`, `Membership`, `Invitation`, `PlatformAdmin`, `User`.
Taşımayanlar: `Role` ve `Permission` (tohumlanmış, değişmeyen katalog).

`created_by` ve `updated_by` her satıra konmaz. Her satıra konan bir "kim" alanı ucuz bir
sahte denetimdir: en son kimin dokunduğunu söyler, ne değiştiğini söylemez. "Kim" yalnızca
işin dilinde gerçekten var olan iki yerdedir: daveti kim gönderdi, daveti kim kabul etti.
Üyeliğe ayrı bir alan konmaz, çünkü o bilginin tamamı kabul edilmiş davet satırındadır.

Gerçek denetim kaydı (append-only log) bu spec'te yoktur; tetikleyicisi ilk "bunu kim
değiştirdi" sorusudur.

### 16. Migration'lar sıfırlanır

Migration'lar lokal Docker dışında hiçbir yerde çalışmadı. Karar 39 bu eşiğin öncesinde
migration'ları düzenlemeye ve sıkıştırmaya izin verir ve bu, o iznin son anıdır.

Her iki bağlamın mevcut migration'ları silinir, yeni modelden tek bir başlangıç
migration'ı üretilir. Lokal veritabanlarının düşürülüp yeniden kurulması gerekir. Bu
spec'ten sonra ileriye doğru migration ve expand/contract disiplini yürürlüğe girer.

## Davet akışı, uçtan uca

```
1. Ayşe (owner) /acme/api/v1/invitations çağırır.
   - invitations.manage yetkisi kontrol edilir
   - rollerin o tenant'ta var olduğu doğrulanır
   - tenant kimliği istekten değil, TenantContext'ten gelir
   - aynı tenant + e-posta için bekleyen davet varsa yeni satır açılmaz, güncellenir
   - Invitation.Create -> InvitationCreated -> outbox satırı, hepsi aynı transaction'da

2. Outbox işçisi Clerk'e gider.
   - Ali'nin hesabı yoksa davet eder; Clerk e-postayı gönderir
   - hesabı varsa bu adım atlanır
   - adım idempotenttir, tekrarı zarar vermez

3. Ali hesabını açar ve giriş yapar. Bu noktada hiçbir yetkisi yoktur.

4. Ali /api/v1/me/invitations çağırır. (üçüncü route yüzeyi: yalnızca kimlik doğrulama)
   - e-posta claim'i yoksa veya doğrulanmamışsa açık hata döner
   - e-postasına gelen bekleyen davetler listelenir

5. Ali /api/v1/me/invitations/{id}/accept çağırır.
   - davet Pending mi, süresi geçmemiş mi
   - davetteki e-posta, token'daki doğrulanmış e-posta ile aynı mı
   - tenant Active mi

6. Önce tenant veritabanı yazılır: kullanıcı yoksa oluşturulur, varsa aktifleştirilir,
   davetteki roller atanır, e-posta yazılır.

7. Sonra control plane yazılır: üyelik eklenir, davet Accepted işaretlenir.
```

Altıncı adımın yedinciden önce gelmesi bilinçlidir. Arada kopma olursa üyelik yazılmamış
olur, yani kişi hâlâ giremez ve tekrar denediğinde işlem tamamlanır. Ters sırada üyelik
olur ama tenant'ta kullanıcı olmaz; bu da anlamsız bir 403 demektir.

## Değişen ve geri alınan kararlar

| Karar | Ne oluyor |
|---|---|
| 16 — Base class'lar yazılmadı | `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent` yazılır. `ValueObject` yazılmaz; `record` onun yerini tutar. |
| 23 — Genel cache katmanı yok | Değişmez. Sizin üretim ortamınızdaki "davet silindi ama cache'te duruyor" sınıfı hata, üyelik ve yetkinin cache'lenmemesi kararını doğrular. |
| 36 — Token'ın SHA-256 özeti | **Geri alınır.** Token tamamen kalkar; kararı doğrulanmış e-posta claim'i verir. |
| Spec 3 / karar 4 — `role_code` | Korunur ve çoğullaşır (`role_codes`). |
| Spec 4 / karar 11 — Son owner domain kuralı | **Geri alınır.** Kural korkuluğa iner, `OwnerRoster` silinir. |
| Spec 3 — "Davet e-postası gönderilmez" | Düşer; Clerk gönderir. |
| 22 — Timezone ve damgalar | Genişler: damga EF interceptor'üne taşınır ve `Membership` ile `User`a da uygulanır. |

## Test stratejisi

xUnit + Testcontainers, gerçek PostgreSQL, AAA yapısı (karar 20). Davranış testi önce
yazılır.

| Test | Kanıtladığı |
|---|---|
| `Entity` eşitliği | Aynı kimlikli iki örnek eşit, farklı tipteki aynı kimlik eşit değil |
| `TenantId.From` | Boş `Guid` reddediliyor |
| `Tenant` geçişleri | İzin verilmeyen her geçiş ayrı ayrı hata veriyor |
| `Invitation.Accept` / `Revoke` | Pending olmayan davet ikinci kez işlenemiyor |
| `InvitationAcceptance` | Kabul edilmemiş davette kabul bilgisi bir bütün olarak yok |
| Davet oluşturma yetkisi | `invitations.manage` olmayan üye 403 alıyor |
| Yinelenen davet | İkinci davet yeni satır açmıyor, mevcut bekleyen satırı güncelliyor |
| Doğrulanmamış e-posta | Kabul reddediliyor, liste sessizce boş dönmüyor |
| E-posta uyuşmazlığı | Başkasının davetinin kimliğini bilmek işe yaramıyor |
| Süresi geçmiş davet | 410 |
| Clerk dağıtımı | Outbox handler'ı iki kez çalıştığında zarar vermiyor |
| Clerk dağıtımı, mevcut hesap | Hesabı olan kullanıcıda adım atlanıyor |
| Kabul, uçtan uca | Ayşe davet ediyor, Ali giriş yapıp kabul ediyor, tenant endpoint'ini çağırabiliyor |
| Permission çözümlemesi | Skip navigation'lar üzerinden aynı claim'ler üretiliyor |
| Son owner korkuluğu | Üç yolun üçü de ayrı ayrı engelleniyor |
| Üye listesi | E-posta dönüyor, liste okunabilir |
| Kimlik sınırı | Değişmeden geçiyor (karar 32) |

Uçtan uca kabul testi bu spec'in de en değerli parçasıdır: davetin oluşmasından davetlinin
tenant endpoint'ini çağırmasına kadar bütün zinciri kanıtlar.

## Bilinçli olarak yapmadıklarımız

| Yapılmayan | Tetikleyici |
|---|---|
| İkinci somut domain event | Kendi gerekçesi olan ilk ihtiyaç; provisioning'in outbox satırını event'e taşımak bunun adayı |
| Roster aggregate'i ve veritabanı seviyesinde son owner koruması | Korkuluğun atlandığı ilk gerçek olay |
| Kilitlenmiş tenant için admin kurtarma işlemi | İlk gerçek kilitlenme |
| Tenant'a özel rol tanımlama | Bir tenant sistem rollerinin yetmediğini söylediğinde |
| Alan adı doğrulama ve otomatik katılma | Kendi IdP'si olan ilk müşteri |
| SCIM ve dizin senkronu | Müşterinin kullanıcı yaşam döngüsünü kendi tarafından yönetmek istemesi |
| Tenant başına "yalnızca şu alan adı" politikası | Bir tenant dışarıdan davete kapanmak istediğinde |
| Gerçek denetim kaydı | İlk "bunu kim değiştirdi" sorusu |
| Açık sign-up | Self-serve tenant kaydı. O gün Clerk davet çağrısı da gereksizleşir |
| `User` üzerinde isim alanı | Üye listesinde insan adı göstermek gerektiğinde |
| Davet e-postasını kendimiz göndermek | Clerk'ün gönderdiği yetmediğinde |
| Üye listesinde sayfalama | Liste tek ekrana sığmadığında |
| IdP soyutlaması (arayüz) | Yok. Koruma tek çağrı yerinden gelir, soyutlamadan değil |

## Clerk tarafının doğrulanmış davranışı

Karar 10 ile 11 Clerk'ün somut davranışına dayanıyor; bu bölüm o davranışı kayda geçirir.

**Oturum token'ı varsayılan olarak e-posta taşımaz.** Claim'ler elle eklenmelidir. Kısa
kodlar `{{user.primary_email_address}}` ve `{{user.email_verified}}`; ikincisi boolean
üretir. Bunlar isimli bir JWT şablonu yerine **varsayılan oturum token'ına** eklenir
(Dashboard içindeki oturum claim editörü). Böylece ön yüzün `getToken({ template })`
çağırması gerekmez ve bütün istemciler aynı token'ı kullanır. Özel claim'ler için
yaklaşık 1.2KB'lık bir sınır var; iki claim bunun çok altında kalır.

Bu, karar 3'ün "Clerk JWT şablonu `email` claim'ini içermek zorunda" cümlesini genişletir:
artık `email_verified` de zorunludur.

**İki uygulama notu.** ASP.NET Core tarafında boolean bir claim `ClaimsPrincipal` içine
`"true"` / `"false"` metni olarak düşer, yani karşılaştırma buna göre yapılır. Claim
eksikse doğrulanmamış sayılır; kapalı tarafa düşülür.

**Claim'ler 60 saniyede bir tazelenir**, yani token'daki veri bir dakikaya kadar bayat
olabilir. Bu, karar 23'ün "yetki ve üyelik cache'lenmez, her istekte sunucuda hesaplanır"
duruşunu doğrudan destekler. E-posta doğrulanmışlığı için bayatlık zararsızdır, çünkü o
değer yalnızca yanlıştan doğruya gider.

**Clerk daveti bir ay sonra sona erer**, bizimki yedi gün (karar 10). Uyumsuzluk bilinçli
kabul edilir: bizim davetimiz düştükten sonra Clerk daveti hâlâ hesap açmaya yeter, ama
hesap açmak tek başına hiçbir tenant'a erişim vermez. Clerk davetini süresi dolduğunda
temizlemek için ayrı bir süpürme işi yazılmaz.

**Davet `redirectUrl` ve `publicMetadata` taşıyabilir.** `redirectUrl` kullanılır, kişi
kabul sonrası doğru sayfaya iner ve bunun için gizli bir değer gerekmez. `publicMetadata`
kullanılmaz; davet aramamız e-postaya dayanır ve Clerk'e ikinci bir bağ eklemenin
karşılığı yoktur.

**Davetin iptali Backend API üzerinden yapılır** ve yalnızca iptal edilmemiş davetler
iptal edilebilir; zaten iptal edilmiş bir davete tekrar denemek hata verir. Outbox
handler'ı bu hatayı başarı sayar, çünkü hedef durum zaten sağlanmıştır.

### Uygulama sırasında doğrulanacak iki nokta

- Restricted modun kapıyı tam olarak neyle açtığı: davetin kendisi mi, yoksa allow-list
  kaydı mı. İkisi de aynı outbox handler'ından tek bir çağrı olur, ama hangisi olduğu
  ilk gerçek çağrıda netleşmeli.
- Var olan bir kullanıcının e-postasına davet oluşturmaya çalışınca dönen hata. Handler
  bunu başarı sayıp adımı atlamalı (karar 11).
- `appsettings.Development.example.json` Clerk yönetim anahtarı için yeni bir
  konfigürasyon anahtarı gerektirir; aynı değişiklikte güncellenmelidir.

## Sonraki adım

Bu spec onaylandıktan sonra test-önce, dosya-dosya plan `docs/superpowers/plans/` altına
yazılır. Her görev kendi test döngüsü ve commit'iyle biter.
