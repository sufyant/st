# Spec 7: Tenant Başına Veritabanı Credential'ı

**Tarih:** 2026-09-13
**Durum:** Onaylandı, uygulanmayı bekliyor
**Kapsam:** `apps/api` — provisioning akışı, bootstrap script'leri, istek yolundaki tenant DB bağlantısı

## Bağlam

Bugün tüm tenant veritabanlarına tek, paylaşılan bir Postgres login rolüyle (`st_tenant`) bağlanılıyor. `GrantTenantAccessAsync` her yeni tenant provision edildiğinde bu role'e o tenant'ın veritabanına `CONNECT` izni **ekliyor**. Zamanla bu rol teorik olarak tüm tenant veritabanlarına bağlanma iznine sahip oluyor.

Bunun açtığı risk, "database-per-tenant, izolasyon bağlantı düzeyinde" kararının (decision 1) iddia ettiği korumadan farklı bir eksende: decision 1'in koruduğu şey uygulama-kodu hataları (unutulmuş filtre vb.) — bu koruma Postgres'in kendi veritabanı-katalog sınırından geliyor, credential paylaşımıyla ilgisi yok. Asıl açık olan senaryo: paylaşılan credential'ın kendisi bir şekilde sızarsa (config dump, log'a düşmüş connection string, yanlış yapılandırılmış backup) — sızdıran kişi doğru `Database=` değerini yazarak **herhangi bir** tenant'a doğrudan bağlanabilir. Bugün blast radius = tüm tenant'lar; hedef blast radius = bir tenant.

## Kapsam

### Bu spec'te
- Sabit (bootstrap) rollerin isimlendirilmesi ve görev ayrımı
- Tenant başına dinamik Postgres rolü + şifre üretimi (provisioning akışı)
- Şifrenin saklanması (`tenant_credentials` tablosu, `IDataProtection` ile şifreli)
- İstek yolunda credential'ın çözülüp kullanılması

### Bu spec'te değil
- Şifre rotasyonu (tetikleyici: ilk gerçek rotasyon ihtiyacı/politikası)
- Admin portalı için ayrı bir DB rolü modeli (platform admin doğrulaması + tüm tenant'larda SELECT) — ayrı bir tasarım konusu, decision 34 ile ilişkili
- Tenant silindiğinde rolün/veritabanının fiilen temizlenmesi — bugün zaten `Tenant.MarkDeleted()` sadece status değiştiriyor, fiziksel `DROP DATABASE`/`DROP ROLE` hiç yazılmamış; bu önceden var olan bir boşluk, bu spec kötüleştirmiyor. Deprovisioning akışı yazılınca bu spec'in kurduğu role de temizlik listesine eklenir.
- Outbox worker'ın ayrı bir deployable'a taşınması (ayrı, bağımsız bir tartışma konusu)

## Kararlar

### 1. Dört sabit rol, `st_` öneki olmadan, göreve göre adlandırılmış

| Eski ad | Yeni ad | Yetki | Kim/nerede çalışır |
|---|---|---|---|
| `st_migrator` | `migrator` | DDL (control plane + tenant şemaları) | Yalnızca `Migrator` CLI projesi — deploy-time, internete hiç açık değil, API process'ine hiç yüklenmez |
| `st_provisioner` | `provisioner` | `CREATEDB` + `CREATEROLE` | `TenantProvisioner` sınıfı — API process'i içinde (`OutboxProcessor` üzerinden) ama tek dar bir sınıfa hapsedilmiş |
| `st_control` | `control` | Control plane'de tam DML, DDL yok | Admin yüzeyi + tenant yüzeyinin control-plane'e yazan handler'ları (CreateInvitation, RevokeMember, AcceptInvitation) |
| `st_tenant` | `resolver` | Control plane'de yalnızca `tenants`/`memberships` tablolarında `SELECT` | `TenantResolver` — her istekte çalışan, URL'den gelen `alias`'ı doğrudan işleyen en riskli kod yolu |

Gerekçe (blast-radius mantığı, iki eksende):
- **Process izolasyonu:** `migrator` hiçbir zaman internete açık, sürekli çalışan bir process'e yüklenmiyor — tek başına deploy aracı. Diğer üçü API process'i içinde birlikte yaşıyor, aralarındaki ayrım process izolasyonundan gelmiyor.
- **Kod-yolu izolasyonu (aynı process içinde bile):** DDL/`CREATEDB`/`CREATEROLE` gibi geri dönüşü olmayan komutlar, request işleyen genel kod yolundan (`control`'ü kullanan onlarca handler) asla erişilebilir olmamalı. Bu yetkiler yalnızca dar, gözden geçirilmiş, tek bir sınıfa (`TenantProvisioner`) hapsedilmiş. Aynı mantıkla `resolver`, sistemin en sık çalışan ve en çok kullanıcı-girdisine yakın kod yolunda (`TenantResolver`) bilerek en düşük yetkiye (salt-okunur, 2 tablo) sahip.

Kanıt: `control` bugün hiç DDL yetkisine sahip değil (`grant-control-plane.sql`'de yalnızca `SELECT, INSERT, UPDATE, DELETE`); migration bu role'e taşınsaydı, API process'i tamamen ele geçirilse bile bugün mümkün olmayan `DROP TABLE` gibi işlemler mümkün olurdu.

### 2. Tenant başına dinamik rol: `access_<tenant-id>`

Veritabanı adıyla (`tenant_<guid-N>`) görsel olarak karışmasın diye farklı önek. Rol adı, `TenantDatabaseName` gibi **veri olarak** `tenant_credentials` tablosunda saklanır, formülden türetilmez (decision 2'nin aynı gerekçesi: adlandırma formatı değişse bile eski tenant'lar kırılmasın).

Yetkisi bugünkü `GrantTenantAccessAsync`'in `st_tenant`'a verdiğiyle birebir aynı — sadece hedef paylaşılan role yerine bu dinamik role: `GRANT CONNECT` (yalnızca o tenant'ın DB'sine), `GRANT SELECT/INSERT/UPDATE/DELETE ON ALL TABLES`, `ALTER DEFAULT PRIVILEGES` (gelecekteki migration'larla eklenecek tablolara da otomatik yetki gitsin diye).

### 3. Provisioning akışı: `GrantAccess` adımı büyüyor

Decision 35'in 5 adımı (`CreateDatabase → MigrateSchema → GrantAccess → SeedOwner → Active`) aynı kalır. `GrantAccess` adımı şunu yapar (idempotent, retry-safe):

1. `pg_roles`'ta `access_<tenant-id>` var mı kontrol et (`CreateDatabaseAsync`'deki `SELECT EXISTS` deseniyle aynı).
2. Yoksa: rastgele şifre üret (`InvitationTokens.Create()`'teki gibi `RandomNumberGenerator`), `CREATE ROLE access_<id> LOGIN PASSWORD '<üretilen>'`.
   Varsa (retry senaryosu — önceki denemede rol yaratıldı ama sonraki adım patladı): `ALTER ROLE ... PASSWORD '<yeni rastgele>'` ile şifreyi yeniden üret. Eski şifreyi geri okuyamayız zaten; idempotency "yeniden üret" ile sağlanır, "hatırla" ile değil.
3. Bugünkü GRANT/ALTER DEFAULT PRIVILEGES adımları, hedef `access_<id>` olacak şekilde çalışır.
4. Şifreyi `IDataProtector` ile şifreleyip `tenant_credentials` tablosuna upsert et (upsert = retry-safe yazma).

`provisioner` rolüne bootstrap script'inde `CREATEROLE` eklenir (zaten `CREATEDB`'si var, aynı "maintenance" karakterine uygun bir ek).

### 4. Saklama: `tenant_credentials` tablosu + `IDataProtection`

Control plane'de yeni tablo: `tenant_id` (FK), `role_name`, `encrypted_password`. `Tenant` aggregate'ine gömülmez — çünkü `Tenant`'ı okuyan çoğu kod yolunun şifreye ihtiyacı yok, yalnızca provisioning ve bağlantı kuran kod okur (Invitation'ın token hash'i gibi ayrı tutma deseniyle aynı gerekçe).

`Microsoft.AspNetCore.DataProtection` kullanılır (ek paket değil, ASP.NET Core'un kendi mekanizması). Key ring dev'de dosya sisteminde, ileride bulut key vault'a taşınabilir — kod değişmeden, yalnızca key ring persist konfigürasyonu değişir.

Bu, "tek sızıntı = tüm tenant'lar" riskini tamamen ortadan kaldırmaz (encryption key sızarsa yine hepsi açılır) ama tek noktadan iki noktaya çıkarır: saldırganın hem control plane DB'sini okuyabilmesi hem de ayrı saklanan key'i ele geçirmesi gerekir.

### 5. İstek yolunda tüketim

Bugün [Program.cs:36-42](apps/api/src/Api/Program.cs:36) sabit bir base connection string'in yalnızca `Database=`'ini değiştiriyor (`factory.Create(tenantContext.DatabaseName)`). Değişiklik:

- Yeni, küçük ve `TenantResolver`'dan ayrı bir servis: tenant'ın `role_name`/`encrypted_password`'ünü control plane'den okur (`resolver` rolüyle — bu tablo da `resolver`'ın SELECT kapsamına eklenir), `IDataProtector` ile çözer. `TenantResolver`'ın 60sn'lik `IMemoryCache` deseniyle aynı şekilde cache'lenir (şifre pratikte hiç değişmediği için TTL daha uzun tutulabilir).
- `TenantDbContextFactory`'ye yeni bir `Create(databaseName, username, password)` overload'u eklenir. Mevcut `Create(databaseName)` **aynen kalır** — `Migrator` ve `TenantProvisioner.MigrateSchemaAsync` hâlâ sabit, geniş yetkili (`migrator`/`provisioner`) rollerle bağlanmaya devam eder; onlar internete açık değil, bu spec'in kapsamı dışında.

### 6. Bootstrap script güncellemeleri

- `bootstrap-roles.sql`: dört rol yeni isimleriyle yaratılır, `provisioner`'a `CREATEROLE` eklenir.
- `grant-control-plane.sql`: `st_tenant` → `resolver`, `tenant_credentials` tablosuna `SELECT` eklenir.
- Tenant başına `CREATE ROLE`/`GRANT` artık bootstrap'te değil, `TenantProvisioner` içinde çalışır (zaten öyleydi, sadece hedef rol adı değişiyor).

## Bilinçli olarak yapmadıklarımız

| Yapılmadı | Tetikleyici |
|---|---|
| Şifre rotasyonu | İlk gerçek rotasyon ihtiyacı/politikası |
| Admin portalı için ayrı DB rolü modeli | Ayrı tasarım — decision 34 ile ilişkili, bu spec'in kapsamına alınmadı |
| Tenant silindiğinde rol/DB'nin fiilen temizlenmesi | Deprovisioning'in fiziksel temizlik akışı yazıldığında (bugün zaten yok) |
| Outbox worker'ın ayrı deployable'a taşınması | Worker'ın kendi kaynak/scale ihtiyacı doğduğunda |

## Not

Bu desen (tenant başına Postgres credential'ı) küçük/orta ölçekli database-per-tenant SaaS'larda en yaygın pratik değil — çoğu sistem paylaşılan bir app-role kullanır, izolasyonu ağ seviyesinde sağlar. Burada bilinçli bir sertleştirme: tek bir credential sızıntısının tek bir tenant'la sınırlı kalması için ek karmaşıklık (N rol, N şifre, provisioning'e yeni adım) kabul edildi.
