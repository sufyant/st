# Spec 4: Application Katmanı, Mediator ve Result

**Tarih:** 2026-09-10
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api`
**Önkoşullar:** [Spec 1 — Control Plane Temeli](2026-09-09-control-plane-temeli-design.md),
[Spec 2 — Tenant Provisioning Workflow](2026-09-09-tenant-provisioning-workflow-design.md),
[Spec 3 — Tenant Üyelik Yaşam Döngüsü](2026-09-10-tenant-membership-lifecycle-design.md)

> Spec 3'ün sonundaki "Spec 4'e devredilenler" başlığı `apps/admin` arayüzünü işaret ediyordu.
> O iş **Spec 5** oldu; bu numara sıradaki backend yapısal kararına verildi.

## Bağlam

Spec 3 ilk gerçek iş yüzeyini üretti ve bunu yaparken karar listesindeki üç maddeyi
(17 CQRS-lite, 18 mediator, 19 Result) bilinçli olarak kapsam dışı bıraktı. O borç şimdi
görünür hale geldi:

- `src/Application` projesi var, çözüme ve `Api.csproj`'a kayıtlı, **içi tamamen boş**.
  Handler'lar için yer açılmış, hiç doldurulmamış.
- İş kuralı transport katmanında yaşıyor. Karar 37'nin son owner koruması
  `MemberEndpoints` içinde `IsLastOwnerAsync` olarak duruyor ve üç ayrı handler'dan
  çağrılıyor. Domain invariant'ı HTTP dışından test edilemiyor.
- Hata sözleşmesi tutarsız. Her endpoint elle `new { error = ... }` anonim nesnesi
  üretiyor, doğrulama bir yerde `try/catch (ArgumentException)` ile yakalanıyor, başka
  yerde hiç yakalanmıyor. `Program.cs` içinde ne merkezî exception handler var ne de
  ProblemDetails kaydı.
- Hiçbir gözlemlenebilirlik yok. Bir isteğin hangi tenant için, hangi kullanıcı adına,
  ne kadar sürdüğü ve nasıl sonuçlandığı hiçbir yere yazılmıyor.

Bu spec o dört boşluğu tek bir yapıyla kapatır ve endpoint'i tek işe indirger:
HTTP'yi bir isteğe çevir, `Result`'ı bir status koduna çevir.

## Kapsam

### Bu spec'te

- `Result<T>` ve hata sınıflandırması
- Elle yazılmış mediator: `ICommand`, `IQuery`, handler arayüzleri, dispatch
- Pipeline behavior'lar: logging, permission, validation, caching, unit of work
- `ICachedQuery` işaretleyici arayüzü ve tenant-kapsamlı cache anahtarı
- Serilog ve istek başına `tenant_id` / `request_id` / `user_id` zenginleştirmesi
- ProblemDetails tabanlı tek hata sözleşmesi
- Mevcut Members, Invitations, Roles ve Me endpoint'lerinin taşınması
- Son owner korumasının domain kuralına dönüşmesi
- Outbox işleyici portu ve `TenantProvisioningHandler`'ın `Application`'a taşınması

### Bu spec'te değil

- **Domain event'ler ve base class'lar.** Karar 16'nın tetikleyicisi hâlâ dolmadı.
- **Provisioning'in mediator'a taşınması.** Handler `Application`'a taşınıyor ama outbox
  tarafından çağrılmaya devam ediyor. Outbox mesajı HTTP isteği değil; permission,
  validation ve unit of work behavior'larının hiçbiri ona uymuyor.
- **Concurrency kontrolü (karar 14) ve dual ID (karar 15).** Tetikleyicileri dolmadı.
- **Seq kurulumu.** Serilog konsola yazar; Seq sink'i bir sonraki adım.

## Kararlar

### 1. Bağımlılık yönü tersine çevrilir: `Application → Infrastructure`

Bugün `Infrastructure.csproj`, `Application`'a referans veriyor. Bu referans hiç
kullanılmıyor, çünkü `Application` boş.

Handler'lar `TenantDbContext` ve `ControlPlaneDbContext` ile doğrudan çalışacak. Bunlar
`Infrastructure` içinde. Dolayısıyla referans ters çevrilir.

Bu, klasik Clean Architecture diyagramına aykırı görünür. Gerekçe projenin kendi
kararında yazılı: Fowler'ın gösterdiği gibi EF Core zaten Data Mapper, Unit of Work ve
Identity Map'tir; bu yüzden Repository ve UnitOfWork sarmalayıcısı yazılmıyor.
Sarmalayıcıyı reddedip yine de "Application, EF'i görmesin" demek, tek amacı bağımlılık
okunu ters çevirmek olan bir port katmanı doğurur. Ousterhout'un ölçüsüyle bu, hiçbir
karmaşıklığı gizlemeyen ama bilişsel yük ekleyen sığ bir soyutlamadır.

Ok yönünü gerçek kılan tek şey vardır ve o korunur: **`Domain` hiçbir şeye referans
vermez.** ASP.NET Core'u da EF Core'u da görmez.

Referans grafiği:

```
Domain     ← Infrastructure ← Application ← Api
                    ↑                        ↑
              ControlPlane ──────────────────┘
```

`Infrastructure` artık `Application`'ı tip olarak göremez. Bunun tek gerçek sonucu
outbox işleyicileridir ve çözümü bir sonraki kararda; derleme zamanı referansı yerine
DI ile ters çevirme.

### 2. Outbox işleyicileri `Application`'a taşınır, port `Infrastructure`'da kalır

Bugün `OutboxProcessor` doğrudan `TenantProvisioningHandler`'ı çözüyor ve `OutboxDrainer`
`message.Type` alanına hiç bakmadan her payload'ı `TenantProvisioningRequested` olarak
deserialize ediyor. Tek mesaj tipi varsayımı koda gömülü; ikinci bir mesaj tipi sessizce
yanlış çalışırdı.

Port `Infrastructure/Messaging` içinde tanımlanır:

```csharp
public interface IOutboxMessageHandler
{
    string MessageType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
```

`OutboxDrainer` artık `IEnumerable<IOutboxMessageHandler>` alır ve mesajı `Type` alanına
göre eşleştirir. Eşleşme yoksa mesaj işlenmiş sayılmaz, `RecordFailure` ile kayda geçer;
kuyruktan sessizce düşmez.

`TenantProvisioningHandler` `Application/Features/Provisioning/` altına taşınır ve bu
arayüzü uygular. Payload deserializasyonu handler'ın kendi işidir, çünkü mesaj tipini
bilen tek yer odur.

Bu, bağımlılık yönü sorusunun cevabıdır ve iddia değil, derleyicinin kanıtladığı bir
şeydir: `Application` `Infrastructure`'a referans verdiği için arayüzü uygulayabilir,
`Infrastructure` ise implementasyonu hiç görmeden DI üzerinden çalıştırır. `IHostedService`
ile aynı şekil.

Handler'ın **mediator'a** taşınması hâlâ bu spec'te değil. Outbox mesajı bir HTTP isteği
değil; permission, validation ve unit of work behavior'larının hiçbiri ona uymuyor.
Taşınan şey handler'ın yeri, çağrılma biçimi değil.

### 3. Vertical slice iki projeye yayılır, dilim bölünmez

Karar 30 vertical slice'ı `src/Api/Features/<Feature>/` olarak yazmıştı. Handler'lar
`Application`'a çıktığı için tanım genişler: bir özellik, aynı adı taşıyan **iki** klasör.

```
src/Application/Features/Members/
    ListMembers.cs              (query + handler + response)
    DisableMember.cs            (command + handler + validator)
    ...
src/Api/Features/Members/
    MemberEndpoints.cs          (route + Result eşlemesi)
```

Dosya başına bir dilim parçası: istek, varsa validator, handler ve yanıt tipi aynı
dosyada durur. Ayrı `Commands/`, `Handlers/`, `Validators/` klasörleri açılmaz; o
bölünme katman-önce klasörlemedir ve vertical slice'ın tam tersidir.

Bir özelliği okumak için iki dosya açılır: dilimin kendisi ve onu HTTP'ye bağlayan
endpoint. Bir özelliği silmek iki klasörü silmektir.

Karar 30'un metni `AGENTS.md` içinde buna göre güncellenir.

### 4. `Result<T>`: beklenen iş hataları exception değildir

```csharp
public readonly record struct Unit;

public enum ErrorKind { Validation, NotFound, Conflict, Forbidden }

public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public IReadOnlyDictionary<string, string[]> Failures { get; init; }
        = new Dictionary<string, string[]>();
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }      // başarısızken erişim InvalidOperationException
    public Error Error { get; }  // başarılıyken erişim InvalidOperationException
}
```

Ayrım keskin tutulur: **beklenen** iş sonucu `Result` döner, **beklenmeyen** durum
exception olarak yükselir. "Rol yok" bir `Result`, "veritabanı düştü" bir exception.

`ErrorKind` bilinçli olarak kısa. Her yeni değer bir HTTP eşlemesi ve bir test borcu
doğurur; listeye ancak gerçek bir handler ihtiyaç duyduğunda eklenir.

`Unit`, hiçbir şey döndürmeyen komutlar için. Ayrı bir generic olmayan `Result` tipi
yazılmaz; iki tip iki kod yolu demektir.

### 5. HTTP eşlemesi tek yerde, ProblemDetails ile

| `ErrorKind` | Status | Gövde |
|---|---|---|
| `Validation` | 400 | `ValidationProblemDetails`, alan bazlı hatalar |
| `NotFound` | 404 | `ProblemDetails` |
| `Conflict` | 409 | `ProblemDetails` |
| `Forbidden` | 403 | `ProblemDetails` |

Eşleme `Api/Http/ResultExtensions.cs` içinde tek bir metotta yaşar. Endpoint'lerde
`Results.BadRequest(new { error = ... })` kalmaz.

Buna ek olarak `AddProblemDetails()` ve `UseExceptionHandler()` kaydedilir: yakalanmayan
bir exception artık gövdesiz 500 değil, `traceId` taşıyan bir ProblemDetails üretir.

Spec 3'ün ürettiği status kodları **korunur**. Davet akışındaki 410 Gone, `ErrorKind`'a
yeni bir değer eklemek yerine ilgili endpoint'te açıkça eşlenir; tek kullanımlık bir
durum için genel bir kategori açmak karar 4'ün kısalığını bozardı.

### 6. Mediator: elle yazılır, çağrı yeri sade kalır

MediatR 2025'te ticari lisansa geçti (karar 18). Yerine yazılan yapı:

```csharp
public interface IRequest<TResponse>;
public interface ICommand<TResponse> : IRequest<TResponse>;
public interface IQuery<TResponse> : IRequest<TResponse>;

public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken);
}
```

`ICommand` ve `IQuery` ayrımı kozmetik değil: behavior'lar buna göre çalışır. Unit of
work yalnızca komutlara, caching yalnızca sorgulara uygulanır. Bu ayrım olmadan her
behavior tip kontrolü yapmak zorunda kalırdı.

`SendAsync` çağrı yerinde ikinci bir generic parametre istemez. Bunun bedeli, istek
tipinden handler'a giden yolun ilk çağrıda reflection ile kurulup
`ConcurrentDictionary` içinde delege olarak önbelleğe alınmasıdır. Alternatif
`SendAsync<TRequest, TResponse>` imzasıydı; C# `TResponse`'u `TRequest`'ten çıkaramadığı
için her çağrı yerinde iki tip adı yazılırdı. Onlarca endpoint'te tekrarlanan gürültü,
elli satırlık tek seferlik bir dispatch'ten pahalıdır.

**Kayda geçiyor:** bu reflection, Native AOT yayımını engeller. Bugün böyle bir hedef
yok; hedef gelirse dispatch kaynak üreteciyle değiştirilir ve çağrı yerleri değişmez.

Handler'lar DI'a assembly taramasıyla değil, `Application/DependencyInjection.cs`
içinde **elle** kaydedilir. Otuz handler'a kadar elle liste okunabilir kalır ve neyin
kayıtlı olduğu görünür olur.

### 7. Pipeline behavior'ları ve sırası

```csharp
public delegate Task<Result<TResponse>> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
```

Dıştan içe sıra:

| # | Behavior | Kapsam | İş |
|---|---|---|---|
| 1 | `LoggingBehavior` | Hepsi | İstek adı, süre, sonuç; hata `ErrorKind`'ı ile |
| 2 | `PermissionBehavior` | `[RequiresPermission]` taşıyanlar | Claim kontrolü, yoksa `Forbidden` |
| 3 | `ValidationBehavior` | Validator'ı olanlar | FluentValidation, hepsi tek `Validation` hatasında toplanır |
| 4 | `CachingBehavior` | `ICachedQuery` | Tenant kapsamlı anahtarla `IMemoryCache` |
| 5 | `UnitOfWorkBehavior` | `ICommand` | Kirli context'leri kaydeder |

Sıra keyfi değil. Permission doğrulamadan **önce** gelir: yetkisiz bir çağırana alan
bazlı doğrulama hataları dönmek, o kişinin göremeyeceği bir sözleşmeyi sızdırır.
Caching doğrulamadan **sonra** gelir, böylece anahtar doğrulanmış girdiden üretilir.

Behavior'lar açık generic olarak (`typeof(LoggingBehavior<,>)`) kaydedilir ve kayıt
sırası zincir sırasını belirler.

### 8. Permission zorlaması endpoint policy'sinden behavior'a taşınır

Karar 18 bunu öngörmüştü. İstek kaydının üstünde:

```csharp
[RequiresPermission(TenantPermissions.MembersManage)]
public sealed record DisableMemberCommand(string ExternalUserId) : ICommand<Unit>;
```

Yetki artık isteğin kendi tanımında durur, route yapılandırmasında değil. Aynı komut
başka bir yüzeyden çağrılsa da yetki kontrolü onunla birlikte gelir.

`RequirePermission(...)` endpoint uzantısı **kaldırılmaz**. Dış kapı olarak kalır:
gövde okunmadan ve model bağlanmadan reddeder, ve OpenAPI şemasında güvenlik gereksinimi
olarak görünür. Behavior içerideki savunmadır. Aynı permission kodunun iki yerde
yazılması bilinçli tekrardır; ikisinin ayrışmadığını bir test kanıtlar.

`PermissionBehavior` claim'lere `ICurrentUser` üzerinden erişir:

```csharp
public interface ICurrentUser
{
    ExternalUserId Id { get; }
    bool HasPermission(string code);
    bool TryGetEmail(out EmailAddress email);
}
```

Bu, `Application` katmanının gördüğü tek ASP.NET Core parçasıdır ve `Api` içinde
`ClaimsPrincipal` üzerinden uygulanır. Dar bir arayüz karşılığında handler'lar
`user.FindFirstValue("sub")` çağırmaktan ve `ClaimsPrincipal` kurmadan test edilebilir
olmaktan kazanır.

`TenantContext` `Api/Tenants/`'dan `Application/Abstractions/`'a taşınır; handler'lar
tenant kimliğini oradan alır. Middleware onu doldurmaya devam eder.

### 9. `ICachedQuery` bugün yazılır, üretimde implementor'ı yoktur

```csharp
public interface ICachedQuery
{
    string CacheKey { get; }
    TimeSpan Duration { get; }
}
```

Karar 23 bugün cache'lenebilir hiçbir sorgu bırakmıyor: permission ve membership
kasıtlı olarak cache dışı, tenant çözümlemesi zaten `TenantResolver` içinde ve
mediator'ın dışında. Behavior bilerek boşta duruyor; ilk uygun sorgu geldiğinde tek
satırlık bir arayüz eklemesiyle devreye girecek.

Dead branch riskini kapatan iki şart var. Birincisi, behavior'ın davranışı test
projesindeki bir sahte sorguyla **kanıtlanır**, iddia edilmez. İkincisi, cache anahtarı
güvenlik açısından kritiktir:

> `CachingBehavior` anahtarı **her zaman** `TenantContext.TenantId` ile öneklendirir ve
> bunu sorgunun kendisine bırakmaz. Sorgu yazarının bunu unutması, bir tenant'ın
> cevabını diğerine servis etmek demektir.

Bu, karar 1'in izolasyon garantisinin ilk kez kod düzeyinde savunulduğu yer. Bir kanıt
testi, iki tenant'ın aynı `CacheKey`'i döndüren aynı sorguyu farklı sonuçlarla almasını
gösterir.

`ICachedQuery` istisnası dışında, karar 23'ün genel cache katmanı reddi yürürlükte kalır.

### 10. Unit of work: iki veritabanı, iki kayıt, dürüst sınır

`UnitOfWorkBehavior` komut tamamlandıktan sonra, sonuç başarılıysa, **kirli olan** her
context'i ayrı ayrı kaydeder. Hata sonucunda hiçbir şey kaydedilmez.

Fowler'da Unit of Work bir veritabanı oturumuna bağlıdır. Ortada iki veritabanı var,
dolayısıyla iki unit of work var. Bunları tek bir isim altında toplamak, çağıranın
bilmek zorunda olduğu bir tehlikeyi gizleyen sığ bir soyutlama olurdu. İki fazlı commit
ise bu projenin reddettiği türden bir altyapı.

Kayıt sırası sabittir ve keyfi değil: **önce control plane, sonra tenant.** Yarıda
kalma senaryosu üyeliğin geri alınmasıdır. Control plane'deki membership silinmiş ama
tenant tarafındaki roller kalkmamışsa, kullanıcı zaten middleware'in membership kapısını
geçemez. Ters sırada ise roller silinmiş bir kullanıcı membership'iyle içeri girer ve
yetkisiz bir üye olarak asılı kalır. Sıra, yarıda kalmanın erişimi **kapatacağı** yönde
seçilmiştir.

**Bilinçli olarak yapılmıyor:** iki yazma arası atomiklik. Tetikleyicisi tabloda.

Domain event kancası bugün takılmıyor ama yeri burasıdır ve şartı şimdiden yazılıyor:
event'ler `SaveChanges`'ten **önce** dispatch edilir. Sonra dispatch etmek, event
handler'ının eklediği outbox satırını iş değişikliğinden ayrı bir transaction'a düşürür
ve karar 24'ün tek şartını bozar. Önce dispatch edildiğinde handler'ın eklediği satır
aynı change tracker'a girer ve aynı commit'le gider.

Sınır ayrıca kayda geçiyor: aggregate event'i **kaydeder**, yayınlamaz. Yayınlamak bir
dispatcher tanımak, dispatcher tanımak da `Domain`'in altyapıyı görmesi demektir. Domain
event hiçbir zaman kalıcı değildir; saklanan tek şey outbox mesajıdır ve o
`Infrastructure`'a aittir, çünkü bir teslimat mekanizmasıdır, domain kavramı değil.

### 11. Son owner koruması domain kuralına dönüşür

Bugün `MemberEndpoints` içindeki `IsLastOwnerAsync`, karar 37'nin invariant'ını üç
handler'da tekrar ediyor ve yalnızca HTTP üzerinden test edilebiliyor.

Kural `Domain/Access/Users/OwnerRoster.cs` içine, aktif owner kimliklerini alan ve
"bu kullanıcı son owner mı" sorusunu cevaplayan saf bir yapıya taşınır. Veritabanı
sorgusu handler'da kalır, karar handler'da alınmaz.

Bu, projedeki ilk gerçek domain kuralıdır ve kendi birim testini kazanır. Feathers'ın
tanımıyla, o kural bugüne kadar legacy koddu.

### 12. Serilog ve istek zenginleştirmesi

Karar 21 uygulanır, Seq hariç. `LoggingBehavior` her istek için `RequestName`,
`Elapsed` ve sonucu yazar. `tenant_id`, `request_id` ve `user_id` bir middleware
tarafından `LogContext`'e itilir, böylece pipeline dışındaki loglar da (middleware,
outbox worker) aynı alanları taşır.

Log satırları asla davet token'ı, e-posta gövdesi ya da bağlantı dizesi taşımaz.

## Endpoint'ten sonra ne kalıyor

Taşımadan sonra tipik bir endpoint:

```csharp
group.MapPost("/{externalUserId}/disable", async (
    string externalUserId,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var result = await mediator.SendAsync(
        new DisableMemberCommand(externalUserId),
        cancellationToken);

    return result.ToNoContent();
})
.RequirePermission(TenantPermissions.MembersManage);
```

Endpoint'te dallanma, `DbContext`, `SaveChanges` ve iş kuralı kalmaz.

Taşınacak yüzey:

| Özellik | İstekler |
|---|---|
| Members | `ListMembersQuery`, `ReplaceMemberRolesCommand`, `RevokeMemberCommand`, `DisableMemberCommand`, `EnableMemberCommand` |
| Invitations | `CreateInvitationCommand`, `ListPendingInvitationsQuery`, `RevokeInvitationCommand` |
| Roles | `ListRolesQuery` |
| Me | `ListMyMembershipsQuery`, `ListMyInvitationsQuery`, `AcceptInvitationCommand` |

`ControlPlane/TenantEndpoints.cs` bu spec'te taşınmaz. Ayrı bir deployable'a çıkma
ihtimali olan bir proje (karar 30), ve tenant oluşturma akışı outbox'a bağlı. Kendi
adımını hak ediyor.

## OpenAPI sözleşmesi

İstek ve yanıt tipleri `Application` katmanına taşındığı için üretilen şema değişir.
Karar 8 gereği `apps/api/openapi/Api.json` ve `packages/api-client` **aynı commit'te**
yeniden üretilir.

Yanıt gövdelerinin **şekli** korunur: taşıma bir yeniden adlandırma turu değil.
Hata gövdeleri ProblemDetails'a geçtiği için orada bilinçli bir değişiklik var, ve
karar 7'nin additive-only disiplini gereği bu `/api/v1` içinde kabul edilir çünkü
bugün hiçbir istemci anonim `{ error }` şeklini okumuyor.

## Test stratejisi

Yeni proje yok. `UnitTests` altyapının kendisini, `IntegrationTests` yüzeyin
korunduğunu kanıtlar.

`UnitTests` (`Application` referansı eklenir):

| Ne | Kanıt |
|---|---|
| `Result<T>` | Başarılıyken `Error`, başarısızken `Value` erişimi patlıyor |
| Mediator dispatch | Doğru handler bulunuyor, kayıtsız istek açık hata veriyor |
| Behavior sırası | Sahte behavior'lar beklenen sırada çalışıyor |
| `ValidationBehavior` | Handler hiç çağrılmıyor, tüm alan hataları tek sonuçta toplanıyor |
| `PermissionBehavior` | Claim yoksa `Forbidden`, handler çağrılmıyor |
| `CachingBehavior` | İkinci çağrı handler'a inmiyor; hata sonucu cache'lenmiyor |
| **Cache tenant izolasyonu** | Aynı `CacheKey`, iki tenant, iki farklı sonuç |
| `UnitOfWorkBehavior` | Hata sonucunda hiçbir context kaydedilmiyor |
| `OwnerRoster` | Tek owner korunuyor, iki owner'dan biri düşürülebiliyor |

`IntegrationTests`: Spec 3'ün mevcut testleri **değiştirilmeden geçmelidir**. Bu, bu
spec'in en önemli kanıtıdır; Feathers'ın characterization test tanımının ta kendisi.
Yalnızca hata gövdesini okuyan assertion'lar ProblemDetails şekline güncellenir.

Eklenenler:

| Ne | Kanıt |
|---|---|
| Yakalanmayan exception | Gövdesiz 500 değil, `traceId` taşıyan ProblemDetails |
| Doğrulama hatası | 400 ve alan bazlı `ValidationProblemDetails` |
| Çift kapı | Endpoint policy ve behavior aynı permission kodunu zorluyor |

## Bilinçli olarak yapmadıklarımız

| Ne | Neden | Tetikleyici |
|---|---|---|
| Repository / UnitOfWork sarmalayıcısı | EF Core zaten odur (Fowler) | Yok. Bu karar kapalı. |
| `Application`'ın EF'ten yalıtılması | Sarmalayıcı yokken tek kazancı ok yönü | Yok |
| İki veritabanı arası atomiklik | İki fazlı commit reddedildi; sıra yarıda kalmayı güvenli yöne çeviriyor | Yarıda kalması güvenli olmayan ilk çoklu veritabanı komutu. Cevap outbox olur (karar 24), dağıtık transaction değil. |
| Handler'ların assembly taramasıyla kaydı | Elle liste okunabilir ve görünür | Kayıt listesinin otuz handler'ı geçmesi |
| Kaynak üreteçli dispatch | Reflection ilk çağrıda bir kez, sonra önbellekli | Native AOT yayım hedefi |
| Genel cache katmanı | Karar 23 yürürlükte | `ICachedQuery`'nin ikinci gerçek implementor'ı |
| Domain event'ler ve base class'lar | Karar 16 yürürlükte; bugün üretilecek hiçbir event'in tüketicisi yok | Davet e-postası. `InvitationCreated` ilk gerçek integration event olur. |
| Seq | Konsol logu bugün yetiyor | Lokal olmayan ilk ortam |
| Provisioning'in mediator'a taşınması | HTTP isteği değil, hiçbir behavior uymuyor | Provisioning'in istek yolundan senkron tetiklenmesi |
| `ControlPlane` endpoint'lerinin taşınması | Ayrı deployable adayı, outbox'a bağlı | Kendi adımı |

## Sonraki adım

Bu spec onaylandıktan sonra test-önce, dosya-dosya plan `docs/superpowers/plans/`
altına yazılır. Her görev kendi test döngüsü ve commit'iyle biter.
