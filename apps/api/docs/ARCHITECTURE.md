# SaaS Template: Mimari Kararlar

## Amaç ve felsefe

Template, her yeni SaaS ürününün sıfırdan değil, hazır ve sağlam bir iskeletten başlaması için kurulur. Odak: backend API ve platformu (`apps/api/`). Web, mobil ve admin istemcileri bu API'yi tüketir; dokümanda yalnızca API'yi şekillendirdikleri yerde geçerler.

- **Yalın başla.** Framework'ün verdiğini yeniden kurma, ihtiyaç gerçekten çıkınca ekle.
- **Olgun ve kanıtlanmış teknoloji.** Moda değil dayanıklılık. Yenilikçilik sadece değer kattığı yerde (Wolverine).
- **Sınırları kodla zorla.** Kurallar doküman değil, derleyici ve mimari testlerle korunur.
- **Güvenlik en dipte.** Tenant izolasyonu uygulama koduna güvenmez, veritabanı garanti eder.
- **Kararlar kayıtlı.** Her mimari karar repoda ADR olarak durur; ajan talimatları kuralları kopyalamaz, ADR'lere referans verir.

## Teknoloji yığını

Backend .NET 10 üzerinde modüler monolit, veritabanı Neon üzerinde tek PostgreSQL projesi.

| Alan | Seçim | Not |
| --- | --- | --- |
| Backend | .NET 10, Minimal API | Tek host, modüller class library |
| Veritabanı | PostgreSQL 18 (Neon), tek proje | Paylaşımlı DB + RLS; testlerde de PostgreSQL 18 (Neon projesinin ana sürümü) |
| Veri erişimi | EF Core code-first | Gerekirse okuma için Dapper |
| Mediator, outbox, mesajlaşma, saga | Wolverine (MIT) | Katman katman benimsenir |
| Tekrar eden işler | Hangfire | Kendi `hangfire` şeması; dashboard şimdilik sadece yerel geliştirmede açık |
| Gerçek zamanlı | SignalR | Redis backplane config ile açılır |
| Kimlik doğrulama | Clerk (Invite-only mod, eski adıyla Restricted) | Sadece authentication; dışarıdan kayıt kapalı; token'ı ASP.NET Core'un JWT bearer handler'ı doğrular |
| E-posta | Resend | Kanal arayüzünün arkasında; geliştirmede sahte kanal |
| Doğrulama | FluentValidation | Pipeline'da |
| Loglama, gözlem | Serilog + OpenTelemetry | OTLP ile dışa aktarım, hedef ortamdan |
| API dokümanı | Scalar | Swagger UI yerine |
| Dış çağrı dayanıklılığı | Microsoft.Extensions.Http.Resilience | Timeout, retry, circuit breaker |
| Test | xUnit, Shouldly, Testcontainers, WebApplicationFactory, NetArchTest | FluentAssertions ticari olduğu için yok |

Lisans notu: MediatR, AutoMapper, MassTransit v9 ve FluentAssertions ticari lisansa geçtiği için kullanılmaz. NetArchTest'in bakımı süren fork'u (`NetArchTest.eNhancedEdition`) ve Testcontainers doğrulandı (0044). Faz 4'te eklenen JWT bearer, `Microsoft.Extensions.Http` ve FluentValidation paketleri kontrol edildi (0002); Clerk SDK'sı kullanılmaz. Hangfire'ın PostgreSQL depolama paketi Faz 6'da lisans ve .NET 10 uyumu açısından doğrulanır.

## Çok kiracılık

Model: tek veritabanı, paylaşımlı tablolar, tenant izolasyonu Postgres Row Level Security (RLS) ile. Tenant çözümleme katmanı soyut tutulur; ileride bir tenant'ı ayrı veritabanına taşımak (hibrit model) mümkün kalır.

**Neden bu model:** Database-per-tenant pratikte onlarca ile birkaç yüz tenant'ta tıkanır; şema-per-tenant iki modelin karmaşasını taşıyıp izolasyonunu vermez. Binlerce tenant'ta sektör paylaşımlı model + satır izolasyonu kullanır.

**Çift kat koruma** (0014). Altyapı paylaşılan Tenancy projesindedir (0048):

1. Uygulama: tenant entity'si tutan modül DbContext'i `TenantDbContext`'ten türer. `ITenantEntity` işaretli her entity'ye model kurulumunda şunlar otomatik eklenir:
   - `TenantId` shadow property'si (kolon `tenant_id`, indeksli; domain görmez).
   - Scope'un `TenantContext`'ine bağlı global query filter. Tenant yoksa hiçbir satır eşleşmez.
2. Veritabanı: aynı işaret, migration'a otomatik RLS yazdırır. Özel bir migration SQL üreticisi tabloya şunları ekler:
   - `ENABLE ROW LEVEL SECURITY`.
   - Okuma için `USING`, yazma için `WITH CHECK` içeren tek bir `tenant_isolation` policy'si.
   - Tenant kolonunun varsayılan değeri aktif tenant ayarından gelir: `NULLIF(current_setting('app.tenant_id', true), '')::uuid`. `NULLIF` gerekir, çünkü ayar bir kez set edildikten sonra oturumda boş string olarak kalır.

Kod filtreyi unutsa da Postgres yanlış satırı döndürmez ve yanlış tenant'a yazmaz. Tenant yokken okuma boş döner, yazma `WITH CHECK`'e takılır.

- **`FORCE ROW LEVEL SECURITY` kapalıdır.** Açılsaydı tablo sahibi de policy'ye takılırdı. Arka plan işlerinin `SECURITY DEFINER` fonksiyonları tam da tenant'lar arası görebilmek için sahip olarak çalışır; `FORCE` altında hiçbir şey göremezlerdi. Bunu aşmanın tek yolu bütün tenant'ları gören bir policy olurdu, o da yasak. Owner kimlik bilgisi yalnızca migration adımında kullanılır.
- **Rol kontrolü.** RLS superuser'a, `BYPASSRLS`'li role ve tablo sahibine işlemez. Bu yüzden `/health/ready` bağlandığı rolü kontrol eder: superuser olmamalı, `BYPASSRLS`'i olmamalı, hiçbir tablonun sahibi olmamalı. Aksi halde pod hazır sayılmaz ve trafik almaz.

**Tenant çözümleme akışı** (0015):

1. İstek gelir, Clerk token'ı doğrulanır.
2. Middleware tenant slug'ını path'ten alır (`/v1/tenants/{slug}/...`), iç tenant id'sine çevirir ve kullanıcının o tenant'a üyeliğini catalog'dan doğrular. Aynı sorgu üyenin rolündeki izinleri de getirir (0030); çözümleme ve yetkilendirme tek sorgudur.
   - Kullanıcı `NameIdentifier` claim'iyle, yani Clerk kullanıcı id'siyle (token'daki `sub`) tanınır.
   - Yalnızca aktif tenant çözülür.
   - Sorgu Tenancy'deki `ITenantDirectory` arkasındadır; host ControlPlane'i tanımaz.
   - Tenant hiçbir zaman istemcinin beyanına güvenilerek seçilmez. İçeride her yerde id dolaşır.
3. Çözümlenen tenant; scope'un `TenantContext`'ine, isteğin Wolverine mesaj zarfına, loglara, trace'e ve HTTP metriklerine konur.
4. Transaction mesaja aittir (0016). Tenant taşıyan her mesaj (istekten, zincirden ya da kuyruktan gelsin) tek bir transaction içinde çalışır. Transaction başında aktif tenant transaction'a yerel olarak set edilir; RLS bu değere göre filtreler. Okumalar da bu kurala dahildir.
   - Tenant endpoint'i modüle tek bir `InvokeAsync` ile gider; böylece isteğin transaction'ı o komutun transaction'ıdır.
   - Transaction'ı host'taki bir Wolverine handler policy'si açar. Handler başarıyla biterse commit eder; istisna ya da başarısız Result dönerse geri alır.
5. Pipeline sırası: routing → kimlik doğrulama → tenant çözümleme (kimseyi reddetmez) → rate limit → yetkilendirme.
   - Kimlik yoksa 401.
   - Bilinmeyen slug, üye olmayan kullanıcı ve aktif olmayan tenant 404 döner; tenant'ın varlığı açığa çıkmaz. Bu 404'ü yetkilendirme verir: çözülmemiş bir tenant rotasındaki ret "yok" olarak cevaplanır.
   - Üyenin endpoint'in istediği izni yoksa 403.
   - Üye olmayanlar da rate limit'ten geçer.
   - Üye olmayan bir system admin de tenant rotalarında 404 alır; tenant'a admin rotalarından girer.
6. Tenant rotalarının kendi segmenti (`tenants`) vardır. Bu yüzden hiçbir slug tenant dışı rotalarla (`/v1/me` gibi) çakışmaz ve yasaklı slug listesi gerekmez (0033).
7. Slug şimdilik değiştirilemez. Yeniden adlandırma ve eski URL yönlendirmesi ihtiyaç çıkınca eklenir. Subdomain'e geçiş çözümleme katmanı değiştirilerek yapılır.

Üyelik sorgusu her istekte çalışır; ölçülüp gerekli görülmeden önbelleğe alınmaz.

**Arka plan işlerinde tenant:** Handler'lar, saga adımları ve zamanlanmış işler HTTP isteği dışında çalışır, yine de RLS'i atlamaz.

- Her mesaj zarfı tenant id'sini taşır (Wolverine'in kendi zarf alanı). Bir handler'dan zincirlenen mesaj, işlenen mesajın tenant'ını devralır. Transaction policy'si onu transaction'a set eder. GUID olmayan bir tenant id'si mesajı başarısız kılar.
- Tenant'lar arası iş bulan işler (Hangfire tarayıcı, sistem temizliği) vadesi gelenleri dar bir `SECURITY DEFINER` fonksiyonla sadece `(tenant_id, id)` olarak çeker ya da tenant'ları catalog'dan gezer; her kaydı kendi tenant'ı altında ayrı işler.
- Fonksiyonu bir migration'dan `TenantScan` yazar (0017):
  - Owner'a aittir, `search_path` sabittir.
  - `EXECUTE` yetkisi `PUBLIC`'ten alınır, yalnızca uygulama rolüne verilir.
- Hiçbir rol `BYPASSRLS` almaz.

**Veritabanı rolleri:**

| Rol | Ad | Amaç |
| --- | --- | --- |
| Owner | `api_owner` | Migration'ları çalıştırır, tabloların sahibidir |
| Uygulama | `api_application` | Hiçbir tablonun sahibi değildir, `BYPASSRLS` yoktur; RLS her zaman geçerlidir |
| Rapor (salt okunur) | `api_reporting` | Sadece system admin rapor endpoint'leri kullanır; yalnızca catalog'u okur, tenant tablolarına erişimi yoktur |

Roller küme seviyesinde olduğu için migration'lardan önce çalışan bir bootstrap script'iyle oluşturulur (`apps/api/db/bootstrap.sql`, 0018).
- Script tekrar çalıştırılabilir; parolalar psql değişkeni olarak verilir.
- Hiçbir role superuser ya da `BYPASSRLS` vermez.
- Yetkiler migration'larla verilir. Migration'lar rol adlarını düz metin olarak yazar.

Tenant verisi üzerinden rapor ihtiyacı çıkarsa ayrı bir ADR ile eklenir.

**Bağlantılar** (0019): İstekler ve mesaj handler'ları havuzlu (pooled) bağlantıyı kullanır (`ConnectionStrings:Pooled`, uygulama rolü). Tenant ayarı transaction'a yerel olduğu için pooler'da başka istemciye sızmaz.

Migration'lar ve oturum seviyesi kilit veya `LISTEN/NOTIFY` gerektiren arka plan bileşenleri (Wolverine dayanıklılık ajanı, Hangfire kilitleri; kurulumda doğrulanır) doğrudan (direct) bağlantıyı kullanır. Migration adımı owner rolüyle `ConnectionStrings:Migrations`'ı kullanır.

Rapor rolünün bağlantısı (`ConnectionStrings:Reporting`) ilk rapor endpoint'iyle Faz 4'te eklendi; `Pooled` gibi ilk kullanımda kontrol edilir. Uygulama rolünün doğrudan bağlantısı Faz 5'te, ilk kullanıldığında eklenir.

**Catalog şeması:** ControlPlane modülünün sahibi olduğu, tenant üstü tek şema. İçinde tenant'lar (slug, durum), kullanıcılar (tek kimlik, Clerk kimliğiyle eşleşir, birden çok tenant'a üye olabilir), tenant-kullanıcı üyelikleri (her biri bir rolle), roller, davetler ve system adminleri durur. RLS yoktur: Golding'e göre kontrol düzleminin kendisi çok kiracılı değildir.

- Tenant'a ait catalog satırlarına (roller, üyelikler, davetler) erişim tek bir tenant kapsamlı giriş noktasından (`TenantCatalog`) geçer ve izolasyon testleriyle korunur. Yalnızca aktif tenant'ın satırlarını (ve hiçbir tenant'a ait olmayan yerleşik rolleri) okur, eklediği üyeliklere aktif tenant'ı basar, başka tenant'ın rolünü ya da davetini eklemeyi reddeder.
- Tenant henüz bilinmeden okuyan okuyucular bilinçli olarak dar tutulur:
  - `TenantDirectory`: tenant çözümlemesi için üyelikleri izinleriyle okur; system admin girişinde tenant'ı slug'ından bulur.
  - `InvitationDirectory`: davet token'ından davetin tenant'ını bulur; yalnızca tenant id'sini verir.
  - `SystemAdminDirectory`: kullanıcının system izinlerini okur.
- Catalog ile tenant şemaları arasında foreign key kurulmaz; tutarlılık uygulama katmanındadır, böylece bir tenant'ı ileride ayrı veritabanına taşımak engellenmez. Catalog içindeki foreign key'ler serbesttir.

## Modüler monolit

Tek deploy edilen bir host, içinde sınırları sıkı modüller. Her modül kendi verisinin, kendi DbContext'inin, kendi migration'larının ve kendi Postgres şemasının sahibidir. Bir modül gerçekten gerekirse ileride ayrı servise koparılır.

**Template modülleri:**

| Modül | Sorumluluk | Şema | RLS |
| --- | --- | --- | --- |
| ControlPlane | Tenant'lar, kullanıcılar, üyelikler, roller ve izinler, davetler, system adminleri, onboarding saga'sı | `catalog` | Yok |
| Notifications | Bildirim kanalları, kullanıcı tanımlı zamanlanmış bildirimler | kendi şeması | Var |
| Audit | Denetim kaydı | kendi şeması | Var |
| SharedKernel | Bağımlılıksız ortak zemin: entity base, `ITenantEntity`, Result ve hata tipleri | yok | yok |
| Tenancy | Modül değil, paylaşılan altyapı (0048): tenant bağlamı, scope başına transaction, `TenantDbContext`, RLS migration üreticisi, `SECURITY DEFINER` kalıbı, `ITenantDirectory` | yok | yok |

Faturalama ve abonelik template'te yoktur; ürün ihtiyaç duyarsa ayrı modül olarak eklenir.

**Proje düzeni** (`apps/api/` altında; ControlPlane/DataPlane gibi gruplama klasörü yok, modüller düz dizilir):

```text
apps/api/
  Api.slnx
  package.json                         # dotnet build ve dotnet test turbo görevleri olarak
  docs/
    ARCHITECTURE.md
    adr/
  src/
    Modules/
      ControlPlane/
        ControlPlane.Contracts/        # diğer modüllere açılan yüz
        ControlPlane.Domain/
        ControlPlane.Application/
        ControlPlane.Infrastructure/   # DbContext, migration, catalog şeması
        ControlPlane.Api/              # endpoint'ler, request/response tipleri, AddControlPlaneModule()
      Notifications/ ...
      Audit/ ...
    Shared/
      SharedKernel/
      Tenancy/                         # EF Core + Npgsql ile tenant izolasyon altyapısı (0048)
    Api/                               # tek çalıştırılabilir host: Program.cs, pipeline, `migrate` komutu
  db/
    bootstrap.sql                      # rolleri oluşturur, migration'lardan önce çalışır
  tests/
    ...                                # bkz. Test stratejisi
```

Her modül, içi ince olsa da beş projeyle kurulur; böylece mimari kurallar her modülde aynıdır (bkz. bilinçli gerilim 1).

`apps/api/package.json` API'yi workspace paketi yapar: `dotnet build` ve `dotnet test` turbo'nun `build` ve `test` görevleri olarak çalışır, böylece kök `pnpm build` ve `pnpm test` API'yi de kapsar. Kök script'te ayrıca `dotnet restore` yoktur.

**Referans kuralları:**

| Proje | Referans verebileceği |
| --- | --- |
| `X.Domain` | .NET temel kütüphanesi ve SharedKernel; üçüncü parti paket yok |
| `X.Contracts` | .NET temel kütüphanesi ve SharedKernel; üçüncü parti paket yok; diğer modüllere geçişli bağımlılık taşımaz |
| `X.Application` | `X.Domain`, `X.Contracts`, diğer modüllerin `*.Contracts`'ı, SharedKernel |
| `X.Infrastructure` | `X.Application`, `X.Domain`, `X.Contracts`, SharedKernel, Tenancy |
| `X.Api` | `X.Application`, `X.Contracts`, `X.Infrastructure` (sadece `AddXModule()` kaydı için), SharedKernel |
| `Api` host | Her modülün `X.Api` projesi, Result'ları Problem Details'e çevirmek için SharedKernel ve tenant pipeline'ı için Tenancy |
| SharedKernel | Sadece .NET temel kütüphanesi |
| Tenancy | SharedKernel ve kalıcılık paketleri (EF Core, Npgsql) |

- Bir modül başka bir modüle sadece `*.Contracts` üzerinden ulaşır.
- Contracts SharedKernel'e referans verebilir; SharedKernel bağımlılıksız olduğu için geçişli bağımlılık kaygısı doğmaz. Böylece senkron sözleşmeler SharedKernel'deki Result tiplerini dönebilir.
- `X.Api` içinde `X.Infrastructure`'a sadece modül kayıt sınıfı (`AddXModule()`) dokunabilir; bu tip seviyesinde bir mimari testle zorlanır.
- Modül içi tipler varsayılan olarak `internal`. `InternalsVisibleTo` sadece aynı modülün projeleri ve test projeleri arasında serbesttir; modüller arası yasaktır ve mimari testle zorlanır.
- **Wolverine istisnası** (0047). Wolverine internal handler'ları, handler metotlarını ve validator'ları bulmaz; internal validator'ı hatasız atlar. Bu yüzden `X.Application`'da şunlar public'tir:
  - Handler'lar, handle ettikleri mesajlar ve FluentValidation validator'ları.
  - Bunların imzasında geçen tipler.
  - Wolverine'in çağırdığı host middleware'leri.

  Gerisi internal kalır. Mimari testler handler ve validator'ların public olmasını zorlar.

  Handler'lar kalıcılığa ve dış sistemlere `X.Application`'daki port arayüzleriyle ulaşır; uygulamaları `X.Infrastructure`'dadır. Port handler imzasında geçtiği için public'tir. Üyeleri domain tiplerini konuşuyorsa üyeler `internal`'dır: domain internal kalır, Wolverine'in ürettiği kod port'u sadece geçirir. Faz 4'te doğrulandı.
- Ortak boru hattı host'ta: auth, tenant çözümleme, hata yönetimi, loglama, validation, transaction, OpenAPI. SharedKernel bu yüzden Wolverine, EF Core veya FluentValidation'a bağımlı değildir.
- Kurallar testte iki yoldan zorlanır, ihlal build'i kırar:
  - Tip bağımlılıkları NetArchTest ile.
  - Proje dosyalarındaki referanslar, test projesinin deps dosyasından okunarak. Kullanılmayan bir referans da yakalanır.

## Modüller arası iletişim ve tip katmanları

Kural: mümkünse event, mecbursa açık sözleşmeyle senkron çağrı, asla başka modülün iç koduna erişim.

- **Event (varsayılan):** Yayıncı modül Contracts'taki bir integration event'i outbox üzerinden yayınlar (`TenantCreatedEvent` gibi). Dinleyenleri bilmez; yeni dinleyici eklemek yayıncıya dokunmaz.
- **Senkron çağrı:** Contracts'ta bir arayüz (`IControlPlaneModule` gibi), gerçek sınıf Infrastructure'da, DI ile bağlanır. Bellek içi metot çağrısıdır, HTTP yoktur.

**Üç ayrı tip katmanı** (birbirine dönüşmez, ayrı tutulur ki biri değişince öteki kırılmasın):

| Katman | Örnek | Kimin için | Nerede |
| --- | --- | --- | --- |
| API tipleri | `InviteMemberRequest`, `TenantResponse` | API istemcileri; OpenAPI'ye yansır | `X.Api` |
| Uygulama tipleri | `InviteMemberCommand` | Modül içi orkestrasyon | `X.Application` |
| Modül sözleşmesi | `TenantSummary`, `IControlPlaneModule`, integration event'ler | Diğer modüller | `X.Contracts` |

SharedKernel farkı: Contracts "modül dışarıya ne sunuyor", SharedKernel "kimsenin malı olmayan ortak zemin". SharedKernel küçük tutulur; iş kuralı içermez.

## Mesajlaşma, saga ve zamanlanmış işler

Wolverine; mediator, transactional outbox ve inbox, modüller arası mesaj ve saga için tek araçtır. Katman katman benimsenir: önce komut ve handler, sonra outbox, sonra saga.

**Outbox ve inbox:** Event'ler iş verisiyle aynı transaction'da yazılır, kaybolmaz. Dayanıklı inbox ve outbox bilinçli olarak açılır; zarf tabloları tek bir `wolverine` şemasında durur. Çok pod'da Postgres satır kilidi ve kilitli satırı atlama sayesinde her mesajı tek pod alır; inbox tekrar gelen mesajı atar. İş anlamında tekrar riski olan mesajlar mantıksal deduplication kimliği taşır.

**Saga:** Koreografi değil orkestrasyon. Saga, akışın sahibi modülde yaşar. Saga durumunu Wolverine saklar, depolama o modülün kendi şemasındadır; elle saga tablosu tasarlanmaz. Her adımın telafisi tanımlıdır.

**Onboarding saga'sı (ControlPlane):** System admin başlatır. Akış: tenant'ı `provisioning` durumunda oluştur, ilk sahip için davet kaydını oluştur, tenant'ı `active` yap, en son davet e-postasını gönder. Bir adım patlarsa telafi tenant'ı `failed` yapar. Davet e-postası son adımdır ve telafi edilmez. Saga'yı kimin tetiklediği ona fark etmez; ileride self-serve kayıt aynı saga'yı public bir endpoint'ten tetikleyerek eklenir.

**Zamanlanmış işler:**

| Tür | Örnek | Nerede yaşar | Kim tetikler |
| --- | --- | --- | --- |
| Sistem tanımlı | Gece temizliği, süresi dolan davetlerin kapatılması | Kodda | Hangfire recurring job |
| Kullanıcı tanımlı | "3 gün sonra hatırlat", "her ayın 1'inde gönder" | Sahibi modülün tenant tablosunda (RLS altında), iptal ve düzenlenebilir | Her dakika çalışan tek bir Hangfire tarayıcı işi vadesi gelenleri işler |

Her iki tür de sunucuda tetiklenir; çok pod'da Hangfire'ın dağıtık kilidi işin tek kez çalışmasını sağlar. Karmaşık takvim kuralı (iş günü, tatil) gerçekten çıkarsa sadece o iş için Quartz eklenir.

## Kimlik, rol ve izinler

Clerk sadece kimliği doğrular ve **Invite-only** (eski adıyla Restricted) modda çalışır: dışarıdan kayıt kapalıdır, hesap sadece davetle açılır. Kullanıcının hangi tenant'ta olduğu, rolü ve izinleri bizim catalog şemamızdadır; böylece auth sağlayıcı değiştirilebilir kalır. Clerk Organizations kullanılmaz. Kimlik token merkezlidir; bütün istemciler aynı kapıdan girer.

Token'ı host doğrular (0028): issuer, imza, süre ve `azp`. `Clerk:AuthorizedParties` API'yi kullanabilecek istemcilerin origin'lerini listeler; Development dışında liste boşsa `/health/ready` unhealthy döner ve pod trafik almaz.

**Davet ve kullanıcı oluşturma (invite-only):**

1. Yetkili bir üye ya da system admin davet oluşturur. Catalog'a bir davet kaydı yazılır: e-posta, tenant, rol, token hash'i (token açık saklanmaz), son geçerlilik tarihi, durum. Davet tek kullanımlıktır.
2. Kişinin Clerk hesabı yoksa API, Clerk backend API'siyle bir Clerk daveti de oluşturur ve kendi davet id'mizi metadata olarak ekler.
3. Kişi linkle kaydolur ya da giriş yapar ve daveti kabul eder.
4. API daveti doğrular: token geçerli olmalı ve Clerk'teki doğrulanmış e-posta davetteki e-postayla eşleşmelidir. Token tek başına yetmez; iletilen bir link başkasını içeri almaz. Kişi catalog'da yoksa kullanıcı kaydını oluşturur ve üyeliği aynı transaction'da ekler.

Catalog'daki kullanıcı kaydı sadece davet kabulünde oluşur; Clerk webhook'u gerekmez. Kaydın olması tek başına yetki vermez, yetki üyelikten gelir.

**Davet e-postasını biz göndeririz** (Faz 4'te doğrulandı, 0029). Clerk davetleri `notify: false` ile Clerk'e e-posta göndertmeden açılır ve dönen `url` bizim e-postamıza konur.

- Clerk hesabı olmayan kişi için Clerk daveti açılır: davet id'miz `public_metadata`'da, kabul linkimiz (`Invitations:AcceptUrl?token=...`) `redirect_url`'de. Kişi bu linkle kaydolur ve kabul sayfasına iner. Hesabı olan kişiye doğrudan kabul linki gider. Hesabın varlığı e-posta adresinin birebir eşleşmesiyle anlaşılır; Clerk'in bazı e-posta filtreleri kısmi eşleşir.
- Token 256 bit rastgeledir, SHA-256 hash'i saklanır; ömrü `Invitations:Lifetime` (varsayılan yedi gün).
- Kabul (`POST /v1/invitations/accept`) tenant dışı rotadır: token davetin tenant'ını bulur, kabul o tenant'ın transaction'ında tek komut olarak çalışır, davet satırı kilitlenir. Doğrulanmış e-postalar Clerk Backend API'sinden okunur; session token e-posta taşımaz.
- Hatalar: bilinmeyen token 404, kullanılmış 409, süresi dolmuş 409, e-posta uyuşmuyor 403, zaten üye 409.
- E-posta, Notifications modülü Resend ile göndermeye başlayana kadar (Faz 6) bir port arkasındadır: Development'ta link loga yazılır, diğer ortamlarda gönderim açık bir hatayla başarısız olur ve davet geri alınır.
- Bizim token'ımız Clerk'te saklanan redirect URL'inde düz metin durur; tek başına erişim vermez, çünkü kabul doğrulanmış e-postayı da ister.

**Üç katman:**

1. **İzin havuzu:** Kodda sabit tanımlı (`members.invite` gibi). Tenant izin üretemez, çünkü izin koddaki gerçek yeteneğe bağlıdır.
2. **Roller:** Yerleşik roller (built-in: sahip, admin, üye, görüntüleyici) her tenant'ta hazır gelir; izinleri kodda sabittir, silinemez ve düzenlenemez. Tenant ayrıca havuzdan seçerek kendi custom rollerini üretir. Her tenant'ta en az bir sahip bulunur; son sahip silinemez ya da rolü düşürülemez.
3. **Üyelik:** Rol kullanıcıya değil, kullanıcı + tenant ikilisine bağlıdır. Aynı kişi bir tenant'ta admin, ötekinde görüntüleyici olabilir.

**Kod rolü değil izni kontrol eder.** "Admin mi?" yerine "Bu izni var mı?" sorulur; custom roller koda dokunmadan çalışır.

**Kimse sahip olmadığını veremez.** Bir rolü vermek, değiştirmek ya da geri almak, ve bir custom rolü şekillendirmek, işe karışan rollerin bütün izinleri yapan kişide de varsa mümkündür. Böylece admin owner yapamaz ve bir owner'ı düşüremez.

**Nasıl çalışır** (0030):

- İzin kataloğu SharedKernel'deki `Permissions`'tadır: tenant havuzu ve system havuzu. Her modülün her katmanı ve host bunu görebilir; endpoint izni buradan adlandırır. Faz 4'te tenant izinleri: `members.invite`, `members.manage`, `owners.manage`, `roles.manage`.
- Yerleşik roller: owner tüm tenant havuzunu, admin `owners.manage` dışındakileri tutar; member ve viewer'ın izni şimdilik yoktur, modüller onlar için yetenek ekledikçe gelir. Yerleşik roller `catalog.roles`'ta hiçbir tenant'a ait olmayan, sabit id'li satırlardır; izinleri koddan gelir. Custom roller tenant'ın satırlarıdır, izinleri metin dizisi olarak durur.
- Kullanımdaki rol silinemez (üyelikler rolü `RESTRICT` ile gösterir; bekleyen daveti olan rolün silinmesi reddedilir).
- Son sahip kuralını `Membership` aggregate'i korur. Üyelik ve rol değiştiren komutlar transaction'larının başında tenant'ın catalog satırını kilitler (`FOR UPDATE`); aynı anda birbirini düşüren iki owner birlikte geçemez.
- Host'ta endpoint gerektirdiği izni policy adı olarak verir: `RequireAuthorization(Permissions.MembersInvite)`. Kayıtlı olmayan her policy adı izin sayılır; yanlış yazılmış bir ad kimseye izin vermez.
- Faz 4 endpoint'leri (`/v1/tenants/{slug}` altında): `POST/PUT/DELETE /roles[/{id}]`, `PUT /members/{userId}/role`, `DELETE /members/{userId}`, `POST /invitations`. Rol, üye ve davet listeleri bir istemci ihtiyaç duyunca eklenir.

Güvenlik karşılığı (OWASP API Top 10): nesne seviyesi yetkilendirmeyi RLS, fonksiyon seviyesini izin sistemi, alan seviyesini ayrı response DTO'ları kapatır. Yetkilendirme davranışı testle doğrulanır.

### System adminleri

System adminleri (Golding'in terimi; SaaS sağlayıcısının kendi personeli) ayrı bir kişi tipi değil, tek kimliğe eklenen ek bir yetkidir.

- **Yer:** catalog şemasında ayrı bir `system_admins` tablosu (kullanıcı, system rolü, kim verdi, ne zaman verdi). `users` tablosuna bayrak konmaz.
- **İlk system admin** kurulumda bir seed script'iyle oluşturulur.
- **Ayrı izin havuzu:** System izinleri (`system.tenants.suspend` gibi) tenant izinlerinden ayrıdır; tenant'ın custom rolleri bunları seçemez.
- **Tenant verisine erişim, tenant bağlamına girerek:** Admin bir tenant seçer, o tenant transaction'a set edilir, RLS normal çalışır. Her giriş audit'e yazılır.
- **Tenant'lar arası rapor:** Sadece admin endpoint'lerinin kullandığı salt okunur rapor rolü (bkz. Veritabanı rolleri). Bu rol yalnızca catalog'u okur.
- **Admin API:** Aynı host içinde ayrı route grubu ve ayrı yetkilendirme politikası. Hangfire dashboard şimdilik sadece yerel geliştirmede açıktır, canlıda kapalıdır; system admin konsolu yapıldığında oradan, system admin iznine bağlı olarak açılır.
- **MFA zorunlu** ve API'de zorlanır: admin route grubu token'da ikinci faktör doğrulamasını arar. Clerk bunu session token'daki `fva` claim'iyle bildirir (Faz 4'te doğrulandı): ilk ve ikinci faktörün doğrulanmasından bu yana geçen dakikalar; ikinci değer `-1` ise ikinci faktör yoktur. Host bunu kendi claim'ine çevirir; token'ın kendisinde aynı adla gelen claim silinir, ikinci faktöre yalnızca `fva` kefil olur.

**Nasıl çalışır** (0031):

- Tek system rolü `Administrator`'dır ve system havuzunun tamamını tutar: `system.tenants.read`, `system.tenants.enter`, `system.members.invite`.
- Seed script'i `apps/api/db/seed-system-admin.sql`'dir (`psql -v external_id=<Clerk kullanıcı id'si>`). Migration'lardan sonra çalışır, tekrar çalıştırılabilir.
- `/v1/admin` system admin ve ikinci faktör ister. `/v1/admin/tenants/{slug}` ayrıca `system.tenants.enter` ister; tenant'ı üyelik aramadan ve durumu ne olursa olsun çözer, tenant'ı istek ve mesaj yoluna normal tenant çözümlemesi gibi koyar.
- Her giriş yetkilendirmeden sonra `Api.Security` log kategorisinde `SystemAdminTenantEntry` güvenlik olayı olarak kaydedilir. Audit kaydı Audit modülüyle (Faz 6) aynı noktaya eklenir.
- Tenant içindeki admin üye değildir; tenant'ın değil kendi rate limit'ini harcar.
- Faz 4 endpoint'leri: `GET /v1/admin/tenants` (tenant'lar durum ve üye sayısıyla, sayfalı, rapor rolüyle) ve `POST /v1/admin/tenants/{slug}/invitations` (her rolle davet; bir tenant'ın ilk sahibi böyle davet edilir).

## API tasarımı

Bütün hatalar tek formatta döner: Problem Details (RFC 9457). İstemciler hatayı tek yerde yorumlar.

- **Beklenen hatalar** ("email zaten kayıtlı", "bulunamadı", "yetki yok") exception değildir; handler bir Result döner, host onu doğru HTTP koduna ve Problem Details'e çevirir.
- **Beklenmeyen hatalar** tek bir global exception handler'da yakalanır; kullanıcıya güvenli genel mesaj, loglara tam detay. Stack trace asla dışarı çıkmaz.
- **Handler'larda try/catch yok.** Loglama, validation, transaction ve performans ölçümü pipeline'da bir kez yazılır, her komut otomatik geçer. Performans ölçümü: komut başına süre histogramı ve config'den gelen eşiği aşınca uyarı logu.
- **Versiyonlama ilk günden, URL segmentiyle.** Tenant kapsamlı rotalar `/v1/tenants/{slug}/...`, tenant dışı rotalar `/v1/me`, `/v1/invitations/...` gibi, admin rotaları `/v1/admin/...`. İstemciler eski sürümde kalabileceği için API eski istemciyi kırmaz.
- **`/v1` altında her endpoint girişli kullanıcı ister;** herkese açık bir endpoint bunu açıkça belirtir. Health check'ler, OpenAPI dokümanı ve Scalar grubun dışındadır.
- **Verimli cevaplar:** sayfalama varsayılan, gereksiz büyük cevap yok. Sayfalama offset'iyle yapılır: `?page=1&pageSize=50`, en büyük sayfa 100; cevap `{ items, page, pageSize, totalCount }` (SharedKernel'deki `PagedList<T>`).
- **Tenant başına rate limit:** Pod içi, bellek tabanlı limit; değerler config'den gelir, plana bağlı değildir. Tenant kovasını yalnızca üyeliği doğrulanmış istek kullanır, anahtar çözümlenmiş tenant id'sidir; route'taki slug asla anahtar olmaz (0035). Tenant'a giren system admin üye değildir, kendi limitini harcar. Diğer isteklerde anahtar, girişli kullanıcıda kullanıcı id'si, girişsiz istekte IP adresidir. Gerçek limit pod sayısıyla çarpılır; bu başlangıç için kabul edilir. Global limit Redis'le ölçeklenince gelir.
- **OpenAPI tek kaynak:** Minimal API'den şema üretilir, XML yorumları açıklamaya girer. Arayüz Scalar. OpenAPI dokümanı `apps/api` altında commit'lenir ki API değişikliği review'da görünsün; istemci tip üretimi istemcinin işidir. `Result` dönen endpoint'lerde dokümana `Result` tipi değil gerçek cevap girer: `Result<T>` için 200 ile `T`, `Result` için 204, hatalar için Problem Details (400, 403, 404, 409). Bunu versiyon grubundaki bir convention sağlar ve test doğrular (0036).

## Bildirimler ve gerçek zamanlı

Notifications modülü "bildirim gönder" der, altındaki kanalı bilmez. Kanallar bir arayüzün arkasında takılabilir:

- **Uygulama içi anlık:** SignalR. Redis backplane config ile açılır, varsayılan kapalıdır; çok pod'a geçerken açılır. Gerekirse aynı arayüz arkasında SSE'ye geçilebilir.
- **E-posta:** Resend adapter'ı. Geliştirme ve testte e-postayı loga yazan sahte kanal.
- **Push:** Şimdilik sadece arayüz ve sahte kanal. Gerçek sağlayıcı istemci belli olunca seçilir.

Yeni kanal eklemek modülün iş mantığını değiştirmez.

## Ölçeklenme, gözlemlenebilirlik, audit ve dayanıklılık

Uygulama Kubernetes'te çok pod'da yatay ölçeklenecek şekilde tasarlanır; hiçbir iş iki pod tarafından çift çalıştırılmaz.

| Konu | Karar |
| --- | --- |
| Çift işleme | Hangfire dağıtık kilit; Wolverine satır kilidi + inbox deduplication; iş anlamında idempotency |
| Durum | Pod durumsuz: dosyalar nesne deposuna, oturum token'da, önbellek Redis'te |
| Bağlantılar | Havuzlu ve doğrudan iki bağlantı (bkz. Çok kiracılık); pod sayısı Postgres limitini patlatmaz |
| Migration | Uygulama başlangıcından ayrı, tek seferlik adım: önce bootstrap script'i, sonra `dotnet Api.dll migrate` (her modülün migration'ları owner rolüyle, 0020) |
| Kapanma | Sağlık kontrolleri (`/health/live`, `/health/ready`: veritabanına erişim, rol kontrolü ve Development dışında Clerk'in izinli istemci listesinin boş olmaması) ve zarif kapanma; pod ölmeden elindeki işi bitirir |
| Gerçek zamanlı | SignalR Redis backplane, config ile açılır |
| Gözlemlenebilirlik | OpenTelemetry (log, metrik, trace) + Serilog; her sinyal tenant id taşır; OTLP ile dışa aktarım hedefi ortamdan, yerelde konsol |
| Dış çağrılar | Timeout, retry, circuit breaker (Microsoft.Extensions.Http.Resilience) |

**Audit log:** Normal logdan ayrıdır; iş verisidir, yıllarca saklanır. Kendi Audit modülünde tek tablo, tenant kolonlu ve RLS korumalı. Komutun modülü audit event'ini kendi transaction'ında outbox'a yazar, Audit modülü tüketip kaydeder; böylece kayıt kaybolmaz ve modül sınırı korunur. Kayda geçenler: başarılı durum değiştiren komutlar, reddedilen yetki denemeleri ve system admin'in tenant'a girişi. Audit modülü gelene kadar (Faz 6) admin girişi yalnızca güvenlik loguna yazılır. Yetki reddinin iş transaction'ı olmadığı için pipeline onu kendi küçük transaction'ında outbox'a yazar. Tenant bağlamı çözülemediyse (üye olmayan biri) red audit tablosuna değil güvenlik loguna düşer, çünkü audit tablosu tenant kapsamlıdır. Kayıt içeriği: kim, ne zaman, hangi tenant, hangi işlem, hangi kayıt, gerekirse önce ve sonra. Sorgular audit'lenmez. Hacim büyürse tarihe göre bölümlenir.

## Test stratejisi ve TDD

İlk günden test-first: kırmızı, yeşil, refactor. Her özellik dışarıdan başarısız bir kabul testiyle başlar, içeri doğru ilerler. Refactor adımı atlanmaz.

**Beş ilke:**

1. Davranışı test et, implementasyonu değil. Test birimi bir sınıf değil bir davranıştır; refactor testleri kırmaz.
2. Domain saf kalır; testleri mock'suz, çok sayıda ve hızlıdır.
3. Kendi veritabanını mock'lama. Postgres, RLS dahil, Testcontainers ile gerçek olarak teste girer. Mock sadece dış sistemlerde (Clerk, e-posta, push).
4. Etkileşim sadece gereksinimse doğrulanır ("bildirim tam bir kez gitmeli").
5. Test kodu üretim kodu kalitesindedir.

**Test projeleri:**

```text
apps/api/tests/
  ControlPlane.UnitTests/
  ControlPlane.IntegrationTests/
  Notifications.UnitTests/
  Notifications.IntegrationTests/
  Audit.IntegrationTests/
  SharedKernel.UnitTests/
  Tenancy.IntegrationTests/       # izolasyon mekanizmasının davranışı, fixture entity üzerinde
  Api.IntegrationTests/           # host davranışı: Problem Details, versiyonlama, rate limit, tenant pipeline, izolasyon kapsaması
  Architecture.Tests/             # modül üstü, NetArchTest
  Onboarding.EndToEndTests/       # çok modüllü akış, yeteneğin adını taşır
```

- Test projesi katman başına değil modül başına; sadece içinde test olan proje açılır. Yukarıdaki liste hedef settir, her proje ilk testi yazıldığında açılır.
- Ağırlık modül integration testlerinde: handler'lar gerçek veritabanıyla test edilir.
- Tenant izolasyon paketi (0042):
  - **Davranış.** Mekanizma `Tenancy.IntegrationTests`'te bir fixture entity üzerinde gerçek Postgres'le kanıtlanır.
  - **Kapsama.** `Api.IntegrationTests`, host'un kaydettiği her modül DbContext'indeki her `ITenantEntity`'yi liste tutmadan bulur. Gerçek migration'lardan sonra her birinin query filter'ını, RLS'ini, tek policy'sini ve tenant kolonu varsayılanını kontrol eder.
  - **Catalog.** Catalog'daki tenant'a ait satırların giriş noktası `ControlPlane.IntegrationTests`'te test edilir.
- Veritabanı gerektiren her test projesi assembly başına bir PostgreSQL 18 container'ı açar; dağıtımdaki gibi önce bootstrap, sonra migration'lar çalışır.
- Testlerde girişli kullanıcı, Clerk'inkine benzeyen ve testin kendi anahtarıyla imzalanmış session token'larıyla gelir; host Clerk'in anahtarları yerine bu anahtara güvenir, doğrulamanın geri kalanı üretim kodudur.
- Dış sistemler (Clerk Backend API, davet e-postası) port'larında sahte uygulamalarla değiştirilir. Gerçek Clerk adapter'ı, isteklerini kaydeden sahte bir HTTP handler'ına karşı ayrıca test edilir.
- Modül entegrasyon testleri handler'ları, host'un transaction policy'si gibi açtıkları bir tenant transaction'ında doğrudan çağırır; modül test projeleri host'u göremez.
- Mimari testler ve tenant izolasyon testleri build'i kırar.

**Yazım kuralları** (şirket ADR-0006'dan uyarlanan):

- Arrange-Act-Assert, boş satırla ayrılmış, etiket yorumu yok.
- Testte `if`, `switch`, döngü, beklenen sonucu üretim mantığıyla hesaplama yok.
- `[Fact]` tek senaryo, `[Theory]` aynı davranışın açık girdileri.
- Builder ve factory'ler taze, geçerli, deterministik nesne döner; senaryoyu belirleyen değerler testte görünür kalır (DAMP öncelikli).
- Zaman `TimeProvider` ile, rastgelelik ve kimlik kontrol altında; sleep, `.Result`, `async void` yok.
- Flaky test retry ile örtülmez, kökünden düzeltilir.
- Hata düzeltmede regresyon testi önce kırmızı yandığı gösterilerek eklenir.
- İsimlendirme davranışı anlatır; test sınıfları davranış alanına göre gruplanır.

## Kaynaklara uyum analizi

Kararların büyük çoğunluğu kaynaklarla doğrudan uyumlu. Altı noktada bilinçli bir gerilim var; hepsi kabul edilmiş ve dengelenmiş.

**Uyumlu kararlar:**

| Karar | Dayandığı kaynak |
| --- | --- |
| Modüler monolitle başla, gerekirse kopar | Richards ve Ford, *Fundamentals of Software Architecture* 2. baskı; Jovanović 2026 rehberleri |
| Modül = bounded context, kendi verisi ve şeması | Evans, *Domain-Driven Design*; Vernon, Khononov |
| Paylaşımlı DB + RLS, tenant detayı merkezde gizli | Golding, *Building Multi-Tenant SaaS Architectures*; Azure multitenant rehberi |
| ControlPlane, system admin, catalog'da RLS yok, onboarding'i tetikleyiciden bağımsız saga | Golding, *Building Multi-Tenant SaaS Architectures*; AWS SaaS Architecture Fundamentals |
| RLS + izin sistemi + ayrı DTO | OWASP API Security Top 10 (BOLA, BFLA, BOPLA) |
| Davet token'ı hash'li, tek kullanımlık, süreli | OWASP Authentication ve Session Management rehberleri |
| Son sahip invariant'ı | Evans, *Domain-Driven Design* (invariant'ı aggregate korur) |
| Outbox, idempotent tüketici, orkestrasyon saga | Richardson, *Microservices Patterns*; Kleppmann, *DDIA* 2. baskı |
| Mimari testler | Ford, Parsons, Kua, *Building Evolutionary Architectures* (fitness function) |
| Problem Details | RFC 9457 |
| Durumsuz pod, config ortamdan, zarif kapanma | Twelve-Factor App |
| Timeout, retry, circuit breaker | Nygard, *Release It!* |
| Audit log, güvenlik olaylarının kaydı | Fowler, Audit Log pattern; OWASP Logging rehberi |
| Saf domain, yan etki kenarda | *Learn You a Haskell*; Khorikov (functional core, imperative shell) |
| TDD döngüsü, dıştan içe | Beck, *TDD by Example*; Freeman ve Pryce, *GOOS* |
| Davranış testi, DB mock'lanmaz | Khorikov, *Unit Testing Principles*; Ian Cooper |
| Test kokuları, builder'lar, AAA | Meszaros, *xUnit Test Patterns*; Martin, *Clean Code* |
| Kararlar ADR olarak | Harmel-Law, *Facilitating Software Architecture* |
| İlk iş uçtan uca ince bir iskelet | Hunt ve Thomas, *The Pragmatic Programmer* (tracer bullet) |
| Önbellek, slug değişimi, global rate limit ihtiyaç çıkınca | Hunt ve Thomas (YAGNI); Ousterhout |

**Bilinçli gerilimler:**

1. **Modül başına beş proje.** Jovanović ile uyumlu, ama Ousterhout'un sığ modül uyarısı ve proje şişmesi riski var. İnce modüllerde (Audit) özellikle hissedilir. Geri dönüşü kolay: gerekirse modül başına bir proje + Contracts'a inilir.
2. **Wolverine'in konvansiyon büyüsü.** Ousterhout belirsizliği karmaşıklığın ana kaynağı sayar. Kademeli benimseme ve açık handler isimlendirmesiyle dengelenir.
3. **ControlPlane ismi.** DDD iş dilinden isim ister; ControlPlane teknik bir terimdir. Golding'in SaaS terminolojisi olduğu için kabul edildi.
4. **Test ağırlığı integration'da.** Şirket ADR-0006 klasik piramidi savunur; Khorikov ve honeycomb yaklaşımı orkestrasyon ağırlıklı kodda integration'ı öne alır. Template ikincisini seçer.
5. **İki zamanlayıcı (Wolverine + Hangfire).** Kitap dayanağı yok, sadelik ilkesine hafif ters. Dashboard ve kullanıcı tanımlı işler için bilinçli seçildi.
6. **Catalog'daki tenant'a ait satırlar.** Roller, üyelikler ve davetler tenant'a ait ama catalog RLS dışında. İzolasyon burada tek giriş noktası ve testlerle uygulama katmanında sağlanır.

**Kaynak bağlantıları:** [Golding, O'Reilly](https://www.oreilly.com/library/view/building-multi-tenant-saas/9781098140632/) · [AWS SaaS Architecture Fundamentals](https://docs.aws.amazon.com/whitepapers/latest/saas-architecture-fundamentals/control-plane-vs.-application-plane.html) · [Fundamentals of Software Architecture 2e, Bölüm 11](https://www.oreilly.com/library/view/fundamentals-of-software/9781098175504/ch11.html) · [Jovanović, modüler monolit adım adım](https://milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step) · [Kleppmann, DDIA 2e](https://martin.kleppmann.com/2026/03/24/designing-data-intensive-applications-2e.html) · [Azure tenancy models](https://learn.microsoft.com/azure/architecture/guide/multitenant/considerations/tenancy-models) · [Wolverine idempotent teslim](https://wolverinefx.net/guide/durability/idempotency.html) · [Clerk Restricted mod](https://clerk.com/changelog/2024-09-30-restricted-sign-up-mode) · [Clerk davetleri](https://clerk.com/docs/guides/users/inviting)

## Kapsam dışı ve kurulumda doğrulanacaklar

Aşağıdakiler kod iskeletini değiştirmediği için sonraya bırakıldı:

- Faturalama ve abonelik
- Self-serve kayıt (onboarding saga'sı buna hazır)
- Slug değiştirme ve eski URL yönlendirmesi
- Gerçek push sağlayıcısı
- Tenant ayar modeli
- İstemci tip üretimi ve istemcilerin iç yapısı; system admin konsolunun arayüzü
- CI/CD ve Kubernetes dağıtımı
- Sır yönetimi (ilke: config ortamdan okunur)
- Gözlem verisinin görüntüleme aracı (Grafana yığını veya Seq)
- Dosya ve medya yönetimi (ilke: nesne deposu, tenant bazlı ayrım)
- E-posta şablonları
- Arama

Kurulumda ilgili fazda doğrulanır, sonuç ADR'ye işlenir. Doğrulananlar:

- Wolverine'in `internal` tiplerle çalışmadığı ve internal üyeli public port'larla çalıştığı (0047).
- NetArchTest fork'u ile Testcontainers'ın lisans ve .NET 10 uyumu (0044).
- Davet e-postasını Resend ile bizim göndereceğimiz; Clerk davetinin `notify: false` ile e-postasız açılabildiği (0029).
- Clerk'in ikinci faktör doğrulamasını `fva` claim'iyle bildirdiği (0031).
- `Result` dönen endpoint'lerin OpenAPI dokümanında doğru tarif edildiği (0036).

Kalanlar:

- Wolverine ve Hangfire'ın havuzlu bağlantıyla mı, doğrudan bağlantıyla mı çalışması gerektiği.
- Hangfire PostgreSQL paketinin lisansı ve .NET 10 uyumu.