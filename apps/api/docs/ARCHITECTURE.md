# SaaS Template: Mimari Kararlar

Oct 3, 2026 · @Sufyan Taskin

## Amaç ve felsefe

Template, her yeni SaaS ürününün sıfırdan değil, hazır ve sağlam bir iskeletten başlaması için kurulur. Odak: backend API ve platformu. Web, mobil ve admin istemcileri bu API'yi tüketir; dokümanda yalnızca API'yi şekillendirdikleri yerde geçerler.

- **Yalın başla.** Framework'ün verdiğini yeniden kurma, ihtiyaç gerçekten çıkınca ekle.
- **Olgun ve kanıtlanmış teknoloji.** Moda değil dayanıklılık. Yenilikçilik sadece değer kattığı yerde (Wolverine).
- **Sınırları kodla zorla.** Kurallar doküman değil, derleyici ve mimari testlerle korunur.
- **Güvenlik en dipte.** Tenant izolasyonu uygulama koduna güvenmez, veritabanı garanti eder.
- **Kararlar kayıtlı.** Her mimari karar repoda ADR olarak durur; ajan talimatları kuralları kopyalamaz, ADR'lere referans verir.

## Teknoloji yığını

Backend .NET 10 üzerinde modüler monolit, frontend Next.js, veritabanı Neon üzerinde tek PostgreSQL projesi.

| Alan | Seçim | Not |
| --- | --- | --- |
| Backend | .NET 10, Minimal API | Tek host, modüller class library |
| Veritabanı | PostgreSQL (Neon), tek proje | Paylaşımlı DB + RLS |
| Veri erişimi | EF Core code-first | Gerekirse okuma için Dapper |
| Mediator, outbox, mesajlaşma, saga | Wolverine (MIT) | Katman katman benimsenir |
| Tekrar eden işler | Hangfire | Dashboard dahil; karmaşık takvim çıkarsa Quartz eklenir |
| Gerçek zamanlı | SignalR | Ölçeklenince Redis backplane |
| Kimlik doğrulama | Clerk | Sadece authentication; tenant ve rol bizim DB'de |
| Doğrulama | FluentValidation | Pipeline'da |
| Loglama, gözlem | Serilog + OpenTelemetry | Görüntüleme aracı sonra seçilir |
| API dokümanı | Scalar | Swagger UI yerine |
| İstemci tip üretimi | Orval / Kiota / openapi-typescript | Tek kaynak backend |
| Dış çağrı dayanıklılığı | Microsoft.Extensions.Http.Resilience | Timeout, retry, circuit breaker |
| Test | xUnit, Shouldly, Testcontainers, WebApplicationFactory, NetArchTest | FluentAssertions ticari olduğu için yok |

Lisans notu: MediatR, AutoMapper, MassTransit v9 ve FluentAssertions ticari lisansa geçtiği için kullanılmaz.

## Çok kiracılık

Model: tek veritabanı, paylaşımlı tablolar, tenant izolasyonu Postgres Row Level Security (RLS) ile. Tenant çözümleme katmanı soyut tutulur; ileride bir tenant'ı ayrı veritabanına taşımak (hibrit model) mümkün kalır.

**Neden bu model:** Database-per-tenant pratikte onlarca ile birkaç yüz tenant'ta tıkanır; şema-per-tenant iki modelin karmaşasını taşıyıp izolasyonunu vermez. Binlerce tenant'ta sektör paylaşımlı model + satır izolasyonu kullanır.

**Çift kat koruma:**

1. Uygulama: `ITenantEntity` işaretli her entity'ye EF Core model kurulumunda tenant kolonu ve global query filter otomatik eklenir.
2. Veritabanı: aynı işaret, migration'a otomatik RLS policy yazdırır. Kod filtreyi unutsa da Postgres yanlış satırı döndürmez.

**Tenant çözümleme akışı:**

1. İstek gelir, Clerk token'ı doğrulanır.
2. Middleware tenant'ı path'ten (`domain/tenant-adi`) alır ve kullanıcının o tenant'a üyeliğini catalog'dan doğrular. Tenant hiçbir zaman istemcinin beyanına güvenilerek seçilmez.
3. Veritabanı işlemi başında aktif tenant bağlantıya transaction kapsamında set edilir; RLS policy bu değere göre filtreler.
4. Subdomain'e geçiş ileride çözümleme katmanı değiştirilerek yapılır.

**Uygulama notları:** uygulamanın DB kullanıcısı tablo sahibi olmaz ve RLS zorunlu tutulur; tenant ayarı bağlantı havuzuyla uyumlu olması için transaction kapsamlı set edilir.

**Catalog şeması:** ControlPlane modülünün sahibi olduğu, tenant üstü tek şema. İçinde tenant'lar, kullanıcılar (tek kimlik, birden çok tenant'a üye olabilir), tenant-kullanıcı üyelikleri, roller ve abonelik bilgisi durur. RLS yoktur, süper admin dünyasıdır. Catalog ile tenant şemaları arasında foreign key kurulmaz; tutarlılık uygulama katmanındadır, böylece bir tenant'ı ileride ayrı veritabanına taşımak engellenmez.

## Modüler monolit

Tek deploy edilen bir host, içinde sınırları sıkı modüller. Her modül kendi verisinin, kendi DbContext'inin, kendi migration'larının ve kendi Postgres şemasının sahibidir. Bir modül gerçekten gerekirse ileride ayrı servise koparılır.

**Template modülleri:**

| Modül | Sorumluluk | Şema | RLS |
| --- | --- | --- | --- |
| ControlPlane | Tenant'lar, kullanıcılar, üyelikler, roller ve izinler, abonelik, onboarding saga'sı | `catalog` | Yok |
| Billing | Tenant içi faturalama | kendi şeması | Var |
| Notifications | Bildirim kanalları, kullanıcı tanımlı zamanlanmış bildirimler | kendi şeması | Var |
| Audit | Denetim kaydı | kendi şeması | Var |
| SharedKernel | Ortak zemin: entity base, `ITenantEntity`, Result ve hata tipleri, pipeline behavior'lar | yok | yok |

**Proje düzeni** (katmanlar ayrı class library; klasör grubu yok, düz yapı):

```text
src/
  Modules/
    ControlPlane/
      ControlPlane.Contracts/        # diğer modüllere açılan yüz
      ControlPlane.Domain/
      ControlPlane.Application/
      ControlPlane.Infrastructure/   # DbContext, migration, catalog şeması
      ControlPlane.Api/              # endpoint'ler + request/response tipleri
    Billing/ ...
    Notifications/ ...
    Audit/ ...
  Shared/
    SharedKernel/
  Api/                               # tek çalıştırılabilir host: Program.cs
tests/
  ...                                # bkz. Test stratejisi
```

**Referans kuralları:**

- Bir modül başka bir modüle sadece `*.Contracts` üzerinden referans verir; Application, Domain, Infrastructure'a asla.
- Modül içi tipler varsayılan olarak `internal`.
- Api host bütün modüllere referans verir ve `AddXModule()` uzantılarıyla kaydeder. Ortak boru hattı orada: auth, tenant çözümleme, hata yönetimi, loglama, OpenAPI.
- Endpoint'ler modülün kendi Api projesinde Minimal API ile tanımlanır, host sadece toplar.
- Kurallar NetArchTest ile testte zorlanır, ihlal build'i kırar.

## Modüller arası iletişim ve tip katmanları

Kural: mümkünse event, mecbursa açık sözleşmeyle senkron çağrı, asla başka modülün iç koduna erişim.

- **Event (varsayılan):** Yayıncı modül Contracts'taki bir integration event'i outbox üzerinden yayınlar (`TenantCreatedEvent` gibi). Dinleyenleri bilmez; yeni dinleyici eklemek yayıncıya dokunmaz.
- **Senkron çağrı:** Contracts'ta bir arayüz (`IControlPlaneModule` gibi), gerçek sınıf Infrastructure'da, DI ile bağlanır. Bellek içi metot çağrısıdır, HTTP yoktur.

**Üç ayrı tip katmanı** (birbirine dönüşmez, ayrı tutulur ki biri değişince öteki kırılmasın):

| Katman | Örnek | Kimin için | Nerede |
| --- | --- | --- | --- |
| API tipleri | `CreateTenantRequest`, `TenantResponse` | Frontend ve mobil; OpenAPI'ye yansır | `X.Api` |
| Uygulama tipleri | `CreateTenantCommand` | Modül içi orkestrasyon | `X.Application` |
| Modül sözleşmesi | `TenantSummary`, `IControlPlaneModule`, integration event'ler | Diğer modüller | `X.Contracts` |

SharedKernel farkı: Contracts "modül dışarıya ne sunuyor", SharedKernel "kimsenin malı olmayan ortak zemin". SharedKernel küçük tutulur; iş kuralı içermez.

## Mesajlaşma, saga ve zamanlanmış işler

Wolverine; mediator, transactional outbox ve inbox, modüller arası mesaj ve saga için tek araçtır. Katman katman benimsenir: önce komut ve handler, sonra outbox, sonra saga.

**Outbox ve inbox:** Event'ler iş verisiyle aynı transaction'da yazılır, kaybolmaz. Dayanıklı inbox ve outbox bilinçli olarak açılır. Çok pod'da Postgres satır kilidi ve kilitli satırı atlama sayesinde her mesajı tek pod alır; inbox tekrar gelen mesajı atar. İş anlamında tekrar riski olan mesajlar mantıksal deduplication kimliği taşır.

**Saga:** Koreografi değil orkestrasyon. Saga, akışın sahibi modülde yaşar (tenant onboarding saga'sı ControlPlane'de). Saga durumunu Wolverine saklar, depolama o modülün kendi şemasındadır; elle saga tablosu tasarlanmaz. Her adım patlarsa telafi adımları tanımlıdır. Örnek akış: tenant oluştur, admin kullanıcı oluştur, varsayılan rol ve ayarlar, hoş geldin bildirimi.

**Zamanlanmış işler:**

| Tür | Örnek | Nerede yaşar | Kim tetikler |
| --- | --- | --- | --- |
| Sistem tanımlı | Gece temizliği, haftalık rapor | Kodda | Hangfire recurring job |
| Kullanıcı tanımlı | "3 gün sonra hatırlat", "her ayın 1'inde gönder" | Sahibi modülün tenant tablosunda (RLS altında), iptal ve düzenlenebilir | Her dakika çalışan tek bir Hangfire tarayıcı işi vadesi gelenleri işler |

Her iki tür de sunucuda tetiklenir; çok pod'da Hangfire'ın dağıtık kilidi işin tek kez çalışmasını sağlar. Karmaşık takvim kuralı (iş günü, tatil) gerçekten çıkarsa sadece o iş için Quartz eklenir.

## Kimlik, rol ve izinler

Clerk sadece kimliği doğrular. Kullanıcının hangi tenant'ta olduğu, rolü ve izinleri bizim catalog şemamızdadır; böylece auth sağlayıcı değiştirilebilir kalır. Kimlik token merkezlidir; web ve mobil aynı kapıdan girer.

**Üç katman:**

1. **İzin havuzu:** Kodda sabit tanımlı (`invoices.delete` gibi). Tenant izin üretemez, çünkü izin koddaki gerçek yeteneğe bağlıdır.
2. **Roller:** Sistem rolleri (sahip, admin, üye, görüntüleyici) her tenant'ta hazır gelir, silinemez. Tenant ayrıca havuzdan seçerek kendi custom rollerini üretir.
3. **Üyelik:** Rol kullanıcıya değil, kullanıcı + tenant ikilisine bağlıdır. Aynı kişi bir tenant'ta admin, ötekinde görüntüleyici olabilir.

**Kod rolü değil izni kontrol eder.** "Admin mi?" yerine "Bu izni var mı?" sorulur; custom roller koda dokunmadan çalışır.

Güvenlik karşılığı (OWASP API Top 10): nesne seviyesi yetkilendirmeyi RLS, fonksiyon seviyesini izin sistemi, alan seviyesini ayrı response DTO'ları kapatır. Yetkilendirme davranışı testle doğrulanır.

## API tasarımı

Bütün hatalar tek formatta döner: Problem Details (RFC 9457). Frontend ve mobil hatayı tek yerde yorumlar.

- **Beklenen hatalar** ("email zaten kayıtlı", "bulunamadı", "yetki yok") exception değildir; handler bir Result döner, host onu doğru HTTP koduna ve Problem Details'e çevirir.
- **Beklenmeyen hatalar** tek bir global exception handler'da yakalanır; kullanıcıya güvenli genel mesaj, loglara tam detay. Stack trace asla dışarı çıkmaz.
- **Handler'larda try/catch yok.** Loglama, validation, transaction ve performans ölçümü pipeline'da bir kez yazılır, her komut otomatik geçer.
- **Versiyonlama ilk günden.** İstemciler eski sürümde kalabileceği için zorunlu; API eski istemciyi kırmaz.
- **Verimli cevaplar:** sayfalama varsayılan, gereksiz büyük cevap yok.
- **Tenant başına rate limit:** gürültülü komşuyu ve kaynak tüketimi saldırısını sınırlar.
- **OpenAPI tek kaynak:** Minimal API'den şema üretilir, XML yorumları açıklamaya girer. Arayüz Scalar. Frontend tipleri ve API fonksiyonları şemadan generate edilir (`npm run generate`).

## Bildirimler ve gerçek zamanlı

Notifications modülü "bildirim gönder" der, altındaki kanalı bilmez. Kanallar bir arayüzün arkasında takılabilir:

- **Uygulama içi anlık:** SignalR. Çok pod'da Redis backplane. Ölçek ya da ihtiyaç değişirse aynı arayüz arkasında SSE'ye geçilebilir.
- **E-posta**
- **Push:** Apple ve Google push servisleri; istemci kapalıyken de ulaşır.

Yeni kanal eklemek modülün iş mantığını değiştirmez.

## Ölçeklenme, gözlemlenebilirlik, audit ve dayanıklılık

Uygulama Kubernetes'te çok pod'da yatay ölçeklenecek şekilde tasarlanır; hiçbir iş iki pod tarafından çift çalıştırılmaz.

| Konu | Karar |
| --- | --- |
| Çift işleme | Hangfire dağıtık kilit; Wolverine satır kilidi + inbox deduplication; iş anlamında idempotency |
| Durum | Pod durumsuz: dosyalar nesne deposuna, oturum token'da, önbellek Redis'te |
| Bağlantılar | Bağlantı havuzlayıcı (Neon pooler / PgBouncer); pod sayısı Postgres limitini patlatmaz |
| Migration | Uygulama başlangıcından ayrı, tek seferlik adım; pod'lar aynı anda migration çalıştırmaz |
| Kapanma | Sağlık kontrolleri ve zarif kapanma; pod ölmeden elindeki işi bitirir |
| Gerçek zamanlı | SignalR Redis backplane |
| Gözlemlenebilirlik | OpenTelemetry (log, metrik, trace) + Serilog; her sinyal tenant id taşır |
| Dış çağrılar | Timeout, retry, circuit breaker (Microsoft.Extensions.Http.Resilience) |

**Audit log:** Normal logdan ayrıdır; iş verisidir, yıllarca saklanır. Kendi Audit modülünde tek tablo, tenant kolonlu ve RLS korumalı. Pipeline üzerinden her komutta otomatik dolar: kim, ne zaman, hangi tenant, hangi işlem, hangi kayıt, gerekirse önce ve sonra. Hacim büyürse tarihe göre bölümlenir.

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
tests/
  Billing.UnitTests/
  Billing.IntegrationTests/
  ControlPlane.UnitTests/
  ControlPlane.IntegrationTests/
  SharedKernel.UnitTests/
  Architecture.Tests/             # modül üstü, NetArchTest
  Onboarding.EndToEndTests/       # çok modüllü akış, yeteneğin adını taşır
```

- Test projesi katman başına değil modül başına.
- Ağırlık modül integration testlerinde: handler'lar gerçek veritabanıyla test edilir.
- Tenant izolasyon paketi: her `ITenantEntity` otomatik olarak "başka tenant'ın verisini göremez" testine girer.
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
| RLS + izin sistemi + ayrı DTO | OWASP API Security Top 10 (BOLA, BFLA, BOPLA) |
| Outbox, idempotent tüketici, orkestrasyon saga | Richardson, *Microservices Patterns*; Kleppmann, *DDIA* 2. baskı |
| Mimari testler | Ford, Parsons, Kua, *Building Evolutionary Architectures* (fitness function) |
| Problem Details | RFC 9457 |
| Durumsuz pod, config ortamdan, zarif kapanma | Twelve-Factor App |
| Timeout, retry, circuit breaker | Nygard, *Release It!* |
| Audit log | Fowler, Audit Log pattern |
| Saf domain, yan etki kenarda | *Learn You a Haskell*; Khorikov (functional core, imperative shell) |
| TDD döngüsü, dıştan içe | Beck, *TDD by Example*; Freeman ve Pryce, *GOOS* |
| Davranış testi, DB mock'lanmaz | Khorikov, *Unit Testing Principles*; Ian Cooper |
| Test kokuları, builder'lar, AAA | Meszaros, *xUnit Test Patterns*; Martin, *Clean Code* |
| Kararlar ADR olarak | Harmel-Law, *Facilitating Software Architecture* |
| İlk iş uçtan uca ince bir iskelet | Hunt ve Thomas, *The Pragmatic Programmer* (tracer bullet) |

**Bilinçli gerilimler:**

1. **Modül başına beş proje.** Jovanović ile uyumlu, ama Ousterhout'un sığ modül uyarısı ve proje şişmesi riski var. Geri dönüşü kolay: gerekirse modül başına bir proje + Contracts'a inilir.
2. **Wolverine'in konvansiyon büyüsü.** Ousterhout belirsizliği karmaşıklığın ana kaynağı sayar. Kademeli benimseme ve açık handler isimlendirmesiyle dengelenir.
3. **ControlPlane ismi.** DDD iş dilinden isim ister; ControlPlane teknik bir terimdir. Golding'in SaaS terminolojisi olduğu için kabul edildi.
4. **Test ağırlığı integration'da.** Şirket ADR-0006 klasik piramidi savunur; Khorikov ve honeycomb yaklaşımı orkestrasyon ağırlıklı kodda integration'ı öne alır. Template ikincisini seçer.
5. **İki zamanlayıcı (Wolverine + Hangfire).** Kitap dayanağı yok, sadelik ilkesine hafif ters. Dashboard ve kullanıcı tanımlı işler için bilinçli seçildi.
6. **Catalog'daki tenant'a ait satırlar.** Roller ve üyelikler tenant'a ait ama catalog RLS dışında. İzolasyon burada sadece uygulama katmanında; testle korunmalı.

**Kaynak bağlantıları:** [Golding, O'Reilly](https://www.oreilly.com/library/view/building-multi-tenant-saas/9781098140632/) · [Fundamentals of Software Architecture 2e, Bölüm 11](https://www.oreilly.com/library/view/fundamentals-of-software/9781098175504/ch11.html) · [Jovanović, modüler monolit adım adım](https://milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step) · [Kleppmann, DDIA 2e](https://martin.kleppmann.com/2026/03/24/designing-data-intensive-applications-2e.html) · [Azure tenancy models](https://learn.microsoft.com/azure/architecture/guide/multitenant/considerations/tenancy-models) · [Wolverine idempotent teslim](https://wolverinefx.net/guide/durability/idempotency.html)

## Kapsam dışı ve açık noktalar

Aşağıdakiler kod iskeletini değiştirmediği için sonraya bırakıldı:

- CI/CD ve Kubernetes dağıtımı
- Sır yönetimi (Key Vault veya eşdeğeri; ilke: config ortamdan okunur)
- Gözlem verisinin görüntüleme aracı (Grafana yığını veya Seq)
- Dosya ve medya yönetimi (ilke: nesne deposu, tenant bazlı ayrım)
- E-posta şablonları
- Arama
- Admin portal ve mobil istemcinin iç yapısı

Açık sorular:

- [ ] Catalog'daki roller ve üyelikler için uygulama katmanı izolasyonu yeterli mi, yoksa bu tablolara da RLS mi konmalı?
- [ ] Süper admin portalı ayrı bir host mu, aynı API'de ayrı bir route grubu mu?
