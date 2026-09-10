# Application Katmanı, Mediator ve Result — Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking. Her görev kendi test döngüsü ve commit'iyle biter.

**Goal:** İş mantığını endpoint'lerden çıkarmak. Endpoint yalnızca HTTP'yi bir isteğe, `Result`'ı bir status koduna çevirir.

**Architecture:** Elle yazılmış mediator, açık generic pipeline behavior'lar, `Result<T>` ve tek bir ProblemDetails sözleşmesi. `Application` `Infrastructure`'a referans verir; `Domain` hiçbir şeye vermez. Outbox işleyicileri `Application`'a taşınır, port `Infrastructure`'da kalır ve ters çevirme DI ile yapılır.

**Tech Stack:** .NET 10, PostgreSQL 18, EF Core 10, FluentValidation, Serilog, xUnit v3 + Microsoft.Testing.Platform, Testcontainers.

**Spec:** [2026-09-10-application-katmani-design.md](../specs/2026-09-10-application-katmani-design.md)
**Önkoşul:** Spec 3 uygulanmış durumda (commit `f9c9b85`).

## Global Constraints

- Hedef framework `net10.0`. `Domain` ASP.NET Core ve EF Core'dan bağımsız kalır.
- Testler xUnit v3 kullanır ve `TestContext.Current.CancellationToken` alır; açık `// Arrange`, `// Act`, `// Assert` etiketleri zorunludur.
- Kalıcılık testleri gerçek PostgreSQL'e karşı Testcontainers ile çalışır.
- Kod yorumları yalnızca aşikâr olmayan kısıtı açıklar.
- **Spec 3'ün mevcut entegrasyon testleri değiştirilmeden geçmelidir.** Tek istisna hata gövdesini okuyan assertion'lardır; onlar ProblemDetails şekline güncellenir.
- Her görev sonunda `dotnet build apps/api/Api.slnx` temiz olmalıdır.
- Test komutu: `dotnet test --solution apps/api/Api.slnx`.

## Dosya Yapısı

**Yeni dosyalar**
- `src/Application/Results/{Unit,Error,Result}.cs`
- `src/Application/Abstractions/{IRequest,IRequestHandler,IMediator,ICurrentUser,ICachedQuery,RequiresPermissionAttribute}.cs`
- `src/Application/Abstractions/TenantContext.cs` — `Api/Tenants/` içinden taşınır
- `src/Application/Mediation/{Mediator,RequestExecutor}.cs`
- `src/Application/Behaviors/{Logging,Permission,Validation,Caching,UnitOfWork}Behavior.cs`
- `src/Application/DependencyInjection.cs`
- `src/Application/Features/<Feature>/<Slice>.cs`
- `src/Domain/Access/Users/OwnerRoster.cs`
- `src/Infrastructure/Messaging/IOutboxMessageHandler.cs`
- `src/Api/Http/{ResultExtensions,RequestEnrichmentMiddleware,CurrentUser}.cs`

**Değiştirilen**
- `src/Application/Application.csproj`, `src/Infrastructure/Infrastructure.csproj` — referans yönü
- `src/Infrastructure/Messaging/{OutboxDrainer,OutboxProcessor}.cs` — tip bazlı dispatch
- `src/Api/Features/**/*Endpoints.cs` — ince endpoint'ler
- `src/Api/Program.cs`, `AGENTS.md`, `apps/api/openapi/Api.json`, `packages/api-client`

---

### Görev 1: Referans yönü ve `TenantContext`'in taşınması

**Files:**
- Modify: `src/Application/Application.csproj`, `src/Infrastructure/Infrastructure.csproj`
- Move: `src/Api/Tenants/TenantContext.cs` → `src/Application/Abstractions/TenantContext.cs`

**Step 1:** `Infrastructure`'dan `Application` referansını kaldır, `Application`'a `Infrastructure` referansı ekle.
**Step 2:** `TenantContext`'i `Application.Abstractions` namespace'ine taşı, `Set` metodunu `public` yap (artık farklı assembly'den çağrılıyor).
**Step 3:** Kullanan yerlerdeki `using`'leri güncelle.

**Verify:** `dotnet build apps/api/Api.slnx` temiz; mevcut testlerin tamamı geçer.

---

### Görev 2: `Result<T>`, `Error`, `Unit`

**Files:**
- Create: `src/Application/Results/{Unit,Error,Result}.cs`
- Test: `tests/UnitTests/Results/ResultTests.cs`

**Interfaces:** `Result<T>.Success(T)`, `Result<T>.Failure(Error)`, `ErrorKind` dört değerli.

**Step 1:** Testleri yaz: başarılıyken `Error` erişimi patlar, başarısızken `Value` erişimi patlar, `Failures` doğrulama hatasında taşınır.
**Step 2:** Tipleri yaz.

**Verify:** `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`

---

### Görev 3: Mediator sözleşmeleri ve dispatch

**Files:**
- Create: `src/Application/Abstractions/{IRequest,IRequestHandler,IMediator}.cs`, `src/Application/Mediation/Mediator.cs`
- Test: `tests/UnitTests/Mediation/MediatorTests.cs`

**Step 1:** Testleri yaz: doğru handler bulunuyor; kayıtsız istek açıklayıcı `InvalidOperationException` veriyor; aynı istek tipi ikinci çağrıda önbellekten çözülüyor.
**Step 2:** `ConcurrentDictionary` ile önbelleklenen delege dispatch'ini yaz.

**Verify:** Yukarıdaki testler geçer.

---

### Görev 4: Pipeline sözleşmesi ve sıra

**Files:**
- Create: `src/Application/Abstractions/IPipelineBehavior.cs`, `src/Application/DependencyInjection.cs`
- Modify: `src/Application/Mediation/Mediator.cs`
- Test: `tests/UnitTests/Mediation/PipelineOrderTests.cs`

**Step 1:** Test yaz: üç sahte behavior kayıt sırasında dıştan içe çalışıyor, handler en içte.
**Step 2:** Zinciri `Mediator` içinde kur; behavior'lar açık generic olarak kaydedilir.

**Verify:** Sıra testi geçer.

---

### Görev 5: `ValidationBehavior`

**Files:**
- Create: `src/Application/Behaviors/ValidationBehavior.cs`
- Modify: `src/Application/Application.csproj` (FluentValidation)
- Test: `tests/UnitTests/Behaviors/ValidationBehaviorTests.cs`

**Step 1:** Test yaz: geçersiz istekte handler hiç çağrılmıyor, tüm alan hataları tek `Validation` hatasında toplanıyor; validator yoksa istek geçiyor.
**Step 2:** Behavior'ı yaz.

**Verify:** Testler geçer.

---

### Görev 6: `ICurrentUser` ve `PermissionBehavior`

**Files:**
- Create: `src/Application/Abstractions/{ICurrentUser,RequiresPermissionAttribute}.cs`, `src/Application/Behaviors/PermissionBehavior.cs`, `src/Api/Http/CurrentUser.cs`
- Test: `tests/UnitTests/Behaviors/PermissionBehaviorTests.cs`

**Step 1:** Test yaz: attribute yoksa geçiyor; claim yoksa `Forbidden` dönüyor ve handler çağrılmıyor; claim varsa geçiyor.
**Step 2:** Behavior'ı ve `ClaimsPrincipal` üzerinden `CurrentUser` uygulamasını yaz.

**Verify:** Testler geçer.

---

### Görev 7: `ICachedQuery` ve `CachingBehavior`

**Files:**
- Create: `src/Application/Abstractions/ICachedQuery.cs`, `src/Application/Behaviors/CachingBehavior.cs`
- Test: `tests/UnitTests/Behaviors/CachingBehaviorTests.cs`

**Step 1:** Testleri yaz. **En kritik olanı:** aynı `CacheKey`'i döndüren aynı sorgu, iki farklı tenant için iki farklı sonuç verir. Ayrıca ikinci çağrı handler'a inmez ve hata sonucu cache'lenmez.
**Step 2:** Behavior'ı yaz; anahtar **behavior tarafından** `TenantContext.TenantId` ile öneklenir.

**Verify:** Tenant izolasyon testi kırmızıdan yeşile döner.

---

### Görev 8: `UnitOfWorkBehavior`

**Files:**
- Create: `src/Application/Behaviors/UnitOfWorkBehavior.cs`
- Test: `tests/IntegrationTests/UnitOfWorkBehaviorTests.cs`

**Step 1:** Test yaz: komut başarılıysa kirli context'ler kaydediliyor; hata sonucunda hiçbir şey kaydedilmiyor; sorgu hiçbir şey kaydetmiyor.
**Step 2:** Behavior'ı yaz. Sıra sabit: önce control plane, sonra tenant.

**Verify:** Testler geçer.

---

### Görev 9: ProblemDetails sözleşmesi

**Files:**
- Create: `src/Api/Http/ResultExtensions.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/IntegrationTests/ProblemDetailsTests.cs`

**Step 1:** Test yaz: yakalanmayan exception `traceId` taşıyan ProblemDetails üretiyor; doğrulama hatası `ValidationProblemDetails` üretiyor.
**Step 2:** `AddProblemDetails()`, `UseExceptionHandler()` ve `Result` → `IResult` eşlemesini yaz.

**Verify:** Testler geçer.

---

### Görev 10: Serilog ve istek zenginleştirmesi

**Files:**
- Create: `src/Application/Behaviors/LoggingBehavior.cs`, `src/Api/Http/RequestEnrichmentMiddleware.cs`
- Modify: `src/Api/Program.cs`, `src/Api/Api.csproj`

**Step 1:** Serilog'u konsol sink'iyle kur; `tenant_id`, `request_id`, `user_id` `LogContext`'e itilir.
**Step 2:** `LoggingBehavior` istek adını, süreyi ve sonucu yazar. Token, e-posta gövdesi ve bağlantı dizesi asla loglanmaz.

**Verify:** Uygulama ayağa kalkar; mevcut entegrasyon testleri geçer.

---

### Görev 11: Outbox işleyici portu

**Files:**
- Create: `src/Infrastructure/Messaging/IOutboxMessageHandler.cs`
- Modify: `src/Infrastructure/Messaging/{OutboxDrainer,OutboxProcessor}.cs`
- Move: `src/Infrastructure/Provisioning/TenantProvisioningHandler.cs` → `src/Application/Features/Provisioning/`
- Test: `tests/IntegrationTests/OutboxDrainerTests.cs`

**Step 1:** Test yaz: mesaj `Type` alanına göre doğru handler'a gidiyor; eşleşmeyen tip `RecordFailure` ile kayda geçiyor, işlenmiş sayılmıyor.
**Step 2:** Portu ekle, drainer'ı tip bazlı dispatch'e çevir, handler'ı taşı ve arayüzü uygula.

**Verify:** Mevcut outbox ve provisioning testleri geçer.

---

### Görev 12: `OwnerRoster` domain kuralı

**Files:**
- Create: `src/Domain/Access/Users/OwnerRoster.cs`
- Test: `tests/UnitTests/Access/OwnerRosterTests.cs`

**Step 1:** Test yaz: tek owner korunuyor; iki owner'dan biri düşürülebiliyor; owner olmayan kullanıcı kuralı tetiklemiyor.
**Step 2:** Kuralı yaz. Sorgu handler'da kalır, karar burada alınır.

**Verify:** Testler geçer.

---

### Görev 13: Members diliminin taşınması

**Files:**
- Create: `src/Application/Features/Members/*.cs`
- Modify: `src/Api/Features/Members/MemberEndpoints.cs`

**Step 1:** Beş isteği ve handler'larını yaz; son owner kuralı `OwnerRoster` üzerinden çağrılır.
**Step 2:** Endpoint'i inceltip `Result` eşlemesine indir.

**Verify:** `MemberEndpointTests` **değiştirilmeden** geçer.

---

### Görev 14: Invitations diliminin taşınması

**Files:**
- Create: `src/Application/Features/Invitations/*.cs`
- Modify: `src/Api/Features/Invitations/InvitationEndpoints.cs`

**Step 1:** Üç isteği yaz; `EmailAddress` doğrulaması `try/catch` yerine validator'a taşınır.
**Step 2:** Endpoint'i incelt.

**Verify:** `InvitationEndpointTests` değiştirilmeden geçer.

---

### Görev 15: Roles ve Me dilimlerinin taşınması

**Files:**
- Create: `src/Application/Features/{Roles,Me}/*.cs`
- Modify: `src/Api/Features/{Roles,Me}/*Endpoints.cs`

**Step 1:** Dört isteği yaz. Davet kabulündeki 410 Gone eşlemesi endpoint'te açıkça korunur.
**Step 2:** Endpoint'leri incelt.

**Verify:** `RoleEndpointTests` ve `InvitationAcceptanceTests` değiştirilmeden geçer.

---

### Görev 16: OpenAPI, client ve AGENTS.md

**Files:**
- Modify: `apps/api/openapi/Api.json`, `packages/api-client`, `AGENTS.md`

**Step 1:** Şemayı ve TypeScript client'ı yeniden üret (karar 8).
**Step 2:** `AGENTS.md`'de karar 17, 18, 19, 21 durumunu ✅'e çevir; karar 23'e `ICachedQuery` notunu, karar 30'a iki projeye yayılan dilim tanımını ekle.

**Verify:** `dotnet test --solution apps/api/Api.slnx` tamamen yeşil.
