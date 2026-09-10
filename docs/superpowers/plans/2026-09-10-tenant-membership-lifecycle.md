# Tenant Üyelik Yaşam Döngüsü — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bir tenant'a ikinci kişinin katılabilmesi: davet etme, kabul etme, rol atama, üyeliği geri alma ve kullanıcıyı devre dışı bırakma.

**Architecture:** Davetler control plane'de e-posta ile anahtarlanır ve token'ın yalnızca özeti saklanır. Kabul, üyelik kapısının arkasına giremeyeceği için ayrı bir kullanıcı-scoped yüzeyde yaşar. İlk gerçek tenant-scoped iş yüzeyi olduğu için permission enforcement ve request başına tek `TenantDbContext` bu spec'te devreye girer.

**Tech Stack:** .NET 10, PostgreSQL 18, EF Core 10, Dapper, Npgsql, xUnit v3 + Microsoft.Testing.Platform, Testcontainers.

**Spec:** [2026-09-10-tenant-membership-lifecycle-design.md](../specs/2026-09-10-tenant-membership-lifecycle-design.md)
**Önkoşul:** Spec 1 ve Spec 2 uygulanmış durumda (commit `f6b9624`).

## Global Constraints

- Hedef framework `net10.0`; tablo ve kolon adları `snake_case`; GUID birincil anahtarlar `ValueGeneratedNever()`.
- Value object'ler EF'e `HasConversion` ile bağlanır; primitive obsession'dan kaçınılır.
- Domain katmanı ASP.NET Core, EF Core ve dış servislerden bağımsız kalır.
- Zaman damgaları `timestamptz` ve UTC; backend `TimeProvider` üzerinden üretir.
- Testler xUnit v3 kullanır ve `TestContext.Current.CancellationToken` alır; açık `// Arrange`, `// Act`, `// Assert` etiketleri zorunludur.
- Kalıcılık testleri gerçek PostgreSQL'e karşı Testcontainers ile çalışır.
- Tenant request yolu control plane'i yalnızca salt okunur credential ile okur. Control plane'e yazma yalnızca `/admin` yüzeyinde ve davet kabulünde olur.
- Permission'lar cache'lenmez; bir rolün geri alınması anında etkili olur.
- Migration'lar deploy adımıdır; uygulama başlangıcında migration çalıştırılmaz.
- Kod yorumları yalnızca aşikâr olmayan kısıtı açıklar.
- Test komutu: `dotnet test --solution apps/api/Api.slnx` (tek proje için `dotnet test --project <csproj>`).

## Dosya Yapısı

**Yeni dosyalar**
- `src/Domain/Access/EmailAddress.cs` — e-posta value object'i
- `src/Domain/Access/Invitation.cs` — davet ve durum geçişleri
- `src/Infrastructure/Access/InvitationTokens.cs` — token üretimi ve özetleme
- `src/Infrastructure/Persistence/ControlPlane/Configurations/InvitationConfiguration.cs`
- `src/Api/Authorization/TenantClaims.cs` — claim adları
- `src/Api/Authorization/PermissionEndpointExtensions.cs` — `RequirePermission`
- `src/Api/Features/Invitations/InvitationEndpoints.cs` — tenant yüzeyi
- `src/Api/Features/Members/MemberEndpoints.cs` — tenant yüzeyi
- `src/Api/Features/Me/MeEndpoints.cs` — kullanıcı yüzeyi
- `tests/IntegrationTests/TenantSurfaceFixture.cs` — ortak tenant yüzeyi kurulumu

**Değiştirilen**
- `src/Domain/Access/SystemAccessCatalog.cs` — `member` rolü
- `src/Api/Tenants/TenantContext.cs` — veritabanı adı
- `src/Api/Tenants/TenantAccessMiddleware.cs` — scoped context ve permission claim'leri
- `src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs` — scoped `TenantDbContext`
- `src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs` — `Invitations`
- `src/Api/Program.cs` — üç endpoint grubunun bağlanması
- `apps/api/AGENTS.md`

---

### Görev 1: Sistem kataloğuna `member` rolü

**Files:**
- Modify: `apps/api/src/Domain/Access/SystemAccessCatalog.cs`
- Test: `apps/api/tests/UnitTests/Access/SystemAccessCatalogTests.cs`

**Interfaces:**
- Produces: `member` kodlu `SystemRoleDefinition`; her tenant veritabanına migration ile yayılan iki rol.

- [ ] **Step 1: Başarısız testi yaz**

`apps/api/tests/UnitTests/Access/SystemAccessCatalogTests.cs` sınıfına ekle:

```csharp
    [Fact]
    public void Roles_ContainAMemberRoleWithReadOnlyPermissions()
    {
        // Arrange
        var readOnlyCodes = new[] { "members.read", "roles.read" };

        // Act
        var member = SystemAccessCatalog.Roles.Single(role => role.Code == "member");

        // Assert
        var granted = SystemAccessCatalog.Permissions
            .Where(permission => member.PermissionIds.Contains(permission.Id))
            .Select(permission => permission.Code)
            .OrderBy(code => code);
        Assert.Equal(readOnlyCodes.OrderBy(code => code), granted);
    }

    [Fact]
    public void Roles_GiveTheOwnerEveryPermission()
    {
        // Arrange & Act
        var owner = SystemAccessCatalog.Roles.Single(role => role.Code == "owner");

        // Assert
        Assert.Equal(SystemAccessCatalog.Permissions.Count, owner.PermissionIds.Count);
    }
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: `Roles_ContainAMemberRoleWithReadOnlyPermissions` FAIL — `member` rolü yok.

- [ ] **Step 3: Rolü ekle**

`SystemAccessCatalog.Roles` listesine, `owner` tanımından sonra ekle:

```csharp
        new(
            Guid.Parse("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310"),
            "member",
            "Member",
            "Read-only access to this tenant.",
            Permissions
                .Where(permission => permission.Code is "members.read" or "roles.read")
                .Select(permission => permission.Id)
                .ToHashSet())
```

- [ ] **Step 4: Migration üret**

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add AddMemberRole \
  --project src/Infrastructure --startup-project src/Api \
  --context TenantDbContext --output-dir Persistence/Tenants/Migrations
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS. `MigrateAsync_SeedsTheSystemAccessCatalog` sayıları katalogdan
okuduğu için kendiliğinden uyum sağlar.

- [ ] **Step 6: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): add a member role to the system access catalog"
```

---

### Görev 2: Request başına tek `TenantDbContext`

**Files:**
- Modify: `apps/api/src/Api/Tenants/TenantContext.cs`
- Modify: `apps/api/src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/PostgresServiceCollectionExtensions.cs`
- Test: `apps/api/tests/IntegrationTests/TenantDbContextScopeTests.cs`

**Interfaces:**
- Produces: `TenantContext.DatabaseName`; `TenantContext.Set(Guid, string, string)`;
  DI'da scoped `TenantDbContext` — çözümlemesi `TenantContext`'e bağlıdır.

- [ ] **Step 1: `TenantContext`'i genişlet**

`apps/api/src/Api/Tenants/TenantContext.cs` dosyasının tamamını şununla değiştir:

```csharp
namespace Api.Tenants;

public sealed class TenantContext
{
    private Guid? tenantId;
    private string? alias;
    private string? databaseName;

    public Guid TenantId => tenantId ?? throw NotResolved();

    public string Alias => alias ?? throw NotResolved();

    public string DatabaseName => databaseName ?? throw NotResolved();

    internal void Set(Guid resolvedTenantId, string resolvedAlias, string resolvedDatabaseName)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
        databaseName = resolvedDatabaseName;
    }

    private static InvalidOperationException NotResolved() =>
        new("Tenant context has not been resolved.");
}
```

- [ ] **Step 2: Scoped `TenantDbContext`'i kaydet**

Kayıt, `Api` projesindeki `TenantContext`'e bağlı olduğu için Infrastructure'da değil
`Program.cs`'de yapılır; Infrastructure `Api`'yi referans almaz.

`apps/api/src/Api/Program.cs` içine, `builder.Services.AddScoped<TenantResolver>();`
satırından sonra ekle:

```csharp
builder.Services.AddScoped(provider =>
{
    var tenantContext = provider.GetRequiredService<TenantContext>();
    var factory = provider.GetRequiredService<TenantDbContextFactory>();

    return factory.Create(tenantContext.DatabaseName);
});
```

`Program.cs` başına `using Infrastructure.Persistence.Tenants;` ekle.

- [ ] **Step 3: Middleware'i scoped context'e geçir**

`TenantAccessMiddleware.InvokeAsync` içinde, membership kontrolünden sonraki bloğu şununla
değiştir. Tenant bağlamı, tenant veritabanına dokunmadan önce doldurulur; böylece scoped
context çözümlenebilir hale gelir ve middleware kendi context'ini yaratmaz.

```csharp
        tenantContext.Set(tenant.Id, tenant.Alias, tenant.DatabaseName);

        var userId = ExternalUserId.Create(externalUserId);
        var tenantDbContext = services.GetRequiredService<TenantDbContext>();
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            context.RequestAborted);

        if (tenantUser?.Status != TenantUserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
```

Dosyanın sonundaki `tenantContext.Set(...)` çağrısını kaldır; artık yukarıda yapılıyor.

- [ ] **Step 4: Testi yaz**

`apps/api/tests/IntegrationTests/TenantDbContextScopeTests.cs`:

```csharp
using Api.Tenants;
using Infrastructure.Persistence.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

public sealed class TenantDbContextScopeTests
{
    [Fact]
    public void ResolvingTenantDbContext_BeforeTheTenantIsResolved_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped(_ => new TenantDbContextFactory("Host=localhost;Username=st_tenant"));
        services.AddScoped(provider =>
        {
            var tenantContext = provider.GetRequiredService<TenantContext>();

            return provider.GetRequiredService<TenantDbContextFactory>()
                .Create(tenantContext.DatabaseName);
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var act = () => scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }
}
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Tüm testler PASS; mevcut tenant erişim testleri davranış değişmediği için yeşil kalır.

- [ ] **Step 6: Commit**

```bash
git add -A apps/api
git commit -m "refactor(api): share one tenant context per request"
```

---

### Görev 3: Permission claim'leri ve endpoint zorlaması

**Files:**
- Create: `apps/api/src/Api/Authorization/TenantClaims.cs`
- Create: `apps/api/src/Api/Authorization/PermissionEndpointExtensions.cs`
- Modify: `apps/api/src/Api/Tenants/TenantAccessMiddleware.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Create: `apps/api/tests/IntegrationTests/TenantSurfaceFixture.cs`
- Test: `apps/api/tests/IntegrationTests/PermissionEnforcementTests.cs`

**Interfaces:**
- Consumes: Görev 2'nin scoped `TenantDbContext`'i.
- Produces: `TenantClaims.Permission`, `TenantClaims.TenantId`;
  `IEndpointConventionBuilder.RequirePermission(string)`; her request'te yüklenen permission claim'leri;
  `TenantSurfaceFixture` test yardımcısı.

- [ ] **Step 1: Claim adlarını ve yardımcıyı yaz**

`apps/api/src/Api/Authorization/TenantClaims.cs`:

```csharp
namespace Api.Authorization;

public static class TenantClaims
{
    public const string TenantId = "tenant_id";

    public const string Permission = "permission";
}
```

`apps/api/src/Api/Authorization/PermissionEndpointExtensions.cs`:

```csharp
namespace Api.Authorization;

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.Permission, permission));
}
```

- [ ] **Step 2: Middleware'e permission yüklemesini ekle**

`TenantAccessMiddleware` içindeki kullanıcı durumu kontrolünden sonra, `await next(context);`
satırından önce, mevcut claim ekleme bloğunu şununla değiştir:

```csharp
        var permissions = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == tenantUser.Id)
            .Join(
                tenantDbContext.RolePermissions,
                assignment => assignment.RoleId,
                rolePermission => rolePermission.RoleId,
                (_, rolePermission) => rolePermission.PermissionId)
            .Join(
                tenantDbContext.Permissions,
                permissionId => permissionId,
                permission => permission.Id,
                (_, permission) => permission.Code)
            .Distinct()
            .ToListAsync(context.RequestAborted);
        var identity = new ClaimsIdentity("Tenant");
        identity.AddClaim(new Claim(TenantClaims.TenantId, tenant.Id.ToString("N")));

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(TenantClaims.Permission, permission));
        }

        context.User.AddIdentity(identity);
```

Dosyanın başına `using Api.Authorization;` ekle.

- [ ] **Step 3: Ortak test fixture'ını yaz**

Bu fixture Görev 3, 5, 6 ve 7'nin tamamına hizmet eder: tenant'ı `Active` olarak kurar,
veritabanını migrate eder, verilen rol koduyla bir kullanıcı ve membership yaratır ve
`sub` ile `email` claim'lerini yapılandırılabilir kılar.

`apps/api/tests/IntegrationTests/TenantSurfaceFixture.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantSurfaceFixture : IAsyncDisposable
{
    public const string Alias = "acme";
    public const string OwnerUserId = "user_owner";
    public const string OwnerEmail = "owner@example.com";

    private readonly PostgreSqlContainer postgres;
    private readonly WebApplicationFactory<Program> factory;

    private TenantSurfaceFixture(
        PostgreSqlContainer postgres,
        WebApplicationFactory<Program> factory,
        Tenant tenant,
        string controlPlaneConnectionString)
    {
        this.postgres = postgres;
        this.factory = factory;
        Tenant = tenant;
        ControlPlaneConnectionString = controlPlaneConnectionString;
        Client = factory.CreateClient();
    }

    public HttpClient Client { get; }

    public Tenant Tenant { get; }

    public string ControlPlaneConnectionString { get; }

    public string ServerConnectionString => postgres.GetConnectionString();

    public static Task<TenantSurfaceFixture> StartAsync() =>
        StartAsync(OwnerUserId, OwnerEmail, "owner");

    public static async Task<TenantSurfaceFixture> StartAsync(
        string externalUserId,
        string? email,
        string? roleCode)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");
        var tenant = Tenant.Create(Guid.CreateVersion7(), TenantAlias.Create(Alias), DateTimeOffset.UtcNow);
        tenant.CompleteProvisioning(DateTimeOffset.UtcNow);

        await using (var context = CreateControlPlaneDbContext(controlPlane))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            context.Tenants.Add(tenant);
            context.Memberships.Add(Membership.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                ExternalUserId.Create(externalUserId)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecuteAsync(connectionString, $"CREATE DATABASE {tenant.DatabaseName.Value}");
        await using (var tenantDbContext =
                     new TenantDbContextFactory(connectionString).Create(tenant.DatabaseName.Value))
        {
            await tenantDbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

            if (roleCode is not null)
            {
                var user = TenantUser.Create(
                    Guid.CreateVersion7(),
                    ExternalUserId.Create(externalUserId),
                    TenantUserStatus.Active);
                tenantDbContext.Users.Add(user);
                var role = await tenantDbContext.Roles.SingleAsync(
                    candidate => candidate.Code == roleCode,
                    TestContext.Current.CancellationToken);
                tenantDbContext.UserRoles.Add(TenantUserRole.Create(user.Id, role.Id));
                await tenantDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ControlPlane", controlPlane);
            builder.UseSetting("ConnectionStrings:ControlPlaneRead", controlPlane);
            builder.UseSetting("ConnectionStrings:TenantData", connectionString);
            builder.UseSetting("ConnectionStrings:Provisioner", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new TestPrincipal(externalUserId, email));
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

        return new TenantSurfaceFixture(postgres, factory, tenant, controlPlane);
    }

    public ControlPlaneDbContext CreateControlPlane() =>
        CreateControlPlaneDbContext(ControlPlaneConnectionString);

    public TenantDbContext CreateTenantDbContext() =>
        new TenantDbContextFactory(ServerConnectionString).Create(Tenant.DatabaseName.Value);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private static ControlPlaneDbContext CreateControlPlaneDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        return new ControlPlaneDbContext(options);
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed record TestPrincipal(string ExternalUserId, string? Email);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new("sub", principal.ExternalUserId) };

            if (principal.Email is not null)
            {
                claims.Add(new Claim("email", principal.Email));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
```

- [ ] **Step 4: Permission testlerini yaz**

`apps/api/tests/IntegrationTests/PermissionEnforcementTests.cs`:

```csharp
using System.Net;
using Xunit;

namespace IntegrationTests;

public sealed class PermissionEnforcementTests
{
    [Fact]
    public async Task TenantEndpoint_ForAnOwner_IsAllowed()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ManageEndpoint_ForAMember_IsForbidden()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            "user_member",
            "member@example.com",
            "member");

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_ForAMember_IsAllowed()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            "user_member",
            "member@example.com",
            "member");

        // Act
        using var response = await fixture.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantEndpoint_ForAUserWithoutRoles_IsForbidden()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            "user_no_roles",
            "nobody@example.com",
            roleCode: null);

        // Act
        using var response = await fixture.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

`roleCode: null` durumunda tenant veritabanında kullanıcı hiç yaratılmaz, dolayısıyla
middleware kullanıcıyı aktif bulamaz ve 403 döner — permission kontrolüne varmadan önce
üyelik kapısı kapanır.

- [ ] **Step 5: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `members` endpoint'i henüz olmadığı için testler 404 alır.

Bu testler Görev 7 tamamlandığında yeşile döner; bu görevde yalnızca fixture ve claim
altyapısı kuruluyor.

- [ ] **Step 6: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): load tenant permissions into the request principal"
```

---

### Görev 4: Davet kaydı ve token'ın özetlenmesi

**Files:**
- Create: `apps/api/src/Domain/Access/EmailAddress.cs`
- Create: `apps/api/src/Domain/Access/Invitation.cs`
- Create: `apps/api/src/Infrastructure/Access/InvitationTokens.cs`
- Create: `apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/InvitationConfiguration.cs`
- Modify: `apps/api/src/Infrastructure/Persistence/ControlPlane/ControlPlaneDbContext.cs`
- Test: `apps/api/tests/UnitTests/Access/EmailAddressTests.cs`
- Test: `apps/api/tests/UnitTests/Access/InvitationTests.cs`
- Test: `apps/api/tests/IntegrationTests/InvitationTokensTests.cs`

**Interfaces:**
- Produces: `EmailAddress.Create(string)`; `Invitation.Create(...)`, `.Accept(...)`, `.Revoke()`,
  `.IsExpired(DateTimeOffset)`; `InvitationTokens.Create()`, `.Hash(string)`;
  `ControlPlaneDbContext.Invitations`; `control.invitations` tablosu.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/UnitTests/Access/EmailAddressTests.cs`:

```csharp
using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void Create_ForAnInvalidAddress_Throws(string value)
    {
        // Arrange & Act
        var act = () => EmailAddress.Create(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Create_NormalisesCaseAndWhitespace()
    {
        // Arrange & Act
        var email = EmailAddress.Create("  Owner@Example.COM ");

        // Assert
        Assert.Equal("owner@example.com", email.Value);
    }

    [Fact]
    public void Equality_IgnoresTheOriginalCase()
    {
        // Arrange & Act
        var first = EmailAddress.Create("owner@example.com");
        var second = EmailAddress.Create("OWNER@EXAMPLE.COM");

        // Assert
        Assert.Equal(first, second);
    }
}
```

`apps/api/tests/UnitTests/Access/InvitationTests.cs`:

```csharp
using Domain.Access;
using Xunit;

namespace UnitTests.Access;

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsPendingAndExpiresAfterTheLifetime()
    {
        // Arrange & Act
        var invitation = CreateInvitation();

        // Assert
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.Null(invitation.AcceptedAt);
    }

    [Fact]
    public void IsExpired_OnlyAfterTheExpiryMoment()
    {
        // Arrange
        var invitation = CreateInvitation();

        // Act & Assert
        Assert.False(invitation.IsExpired(Now.AddDays(7)));
        Assert.True(invitation.IsExpired(Now.AddDays(7).AddSeconds(1)));
    }

    [Fact]
    public void Accept_StampsTheAcceptingUser()
    {
        // Arrange
        var invitation = CreateInvitation();
        var acceptedBy = ExternalUserId.Create("user_invited");

        // Act
        invitation.Accept(acceptedBy, Now.AddHours(1));

        // Assert
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal(acceptedBy, invitation.AcceptedByExternalUserId);
        Assert.Equal(Now.AddHours(1), invitation.AcceptedAt);
    }

    [Fact]
    public void Accept_Twice_Throws()
    {
        // Arrange
        var invitation = CreateInvitation();
        invitation.Accept(ExternalUserId.Create("user_invited"), Now);

        // Act
        var act = () => invitation.Accept(ExternalUserId.Create("user_other"), Now);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Revoke_APendingInvitation_Succeeds()
    {
        // Arrange
        var invitation = CreateInvitation();

        // Act
        invitation.Revoke(Now);

        // Assert
        Assert.Equal(InvitationStatus.Revoked, invitation.Status);
    }

    [Fact]
    public void Revoke_AnAcceptedInvitation_Throws()
    {
        // Arrange
        var invitation = CreateInvitation();
        invitation.Accept(ExternalUserId.Create("user_invited"), Now);

        // Act
        var act = () => invitation.Revoke(Now);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    private static Invitation CreateInvitation() => Invitation.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EmailAddress.Create("invited@example.com"),
        "member",
        "token-hash",
        ExternalUserId.Create("user_owner"),
        Now,
        TimeSpan.FromDays(7));
}
```

`apps/api/tests/IntegrationTests/InvitationTokensTests.cs`:

```csharp
using Infrastructure.Access;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationTokensTests
{
    [Fact]
    public void Create_ProducesDistinctUrlSafeTokens()
    {
        // Arrange & Act
        var first = InvitationTokens.Create();
        var second = InvitationTokens.Create();

        // Assert
        Assert.NotEqual(first, second);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
        Assert.True(first.Length >= 32);
    }

    [Fact]
    public void Hash_IsStableAndHidesTheToken()
    {
        // Arrange
        var token = InvitationTokens.Create();

        // Act
        var hash = InvitationTokens.Hash(token);

        // Assert
        Assert.Equal(hash, InvitationTokens.Hash(token));
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Derleme hatası — `EmailAddress`, `Invitation` ve `InvitationTokens` yok.

- [ ] **Step 3: `EmailAddress`'i yaz**

`apps/api/src/Domain/Access/EmailAddress.cs`:

```csharp
namespace Domain.Access;

public sealed record EmailAddress
{
    private const int MaximumLength = 320;

    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    public static EmailAddress Create(string value)
    {
        var normalised = value?.Trim().ToLowerInvariant();
        var atIndex = normalised?.IndexOf('@') ?? -1;

        if (string.IsNullOrWhiteSpace(normalised) ||
            normalised.Length > MaximumLength ||
            atIndex <= 0 ||
            atIndex == normalised.Length - 1 ||
            normalised.LastIndexOf('@') != atIndex)
        {
            throw new ArgumentException("Email address is not valid.", nameof(value));
        }

        return new EmailAddress(normalised);
    }
}
```

- [ ] **Step 4: `Invitation`'ı yaz**

`apps/api/src/Domain/Access/Invitation.cs`:

```csharp
namespace Domain.Access;

public enum InvitationStatus { Pending, Accepted, Revoked }

public sealed class Invitation
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public EmailAddress Email { get; private set; } = null!;

    public string RoleCode { get; private set; } = null!;

    public string TokenHash { get; private set; } = null!;

    public InvitationStatus Status { get; private set; }

    public ExternalUserId InvitedByExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public ExternalUserId? AcceptedByExternalUserId { get; private set; }

    private Invitation()
    {
    }

    public static Invitation Create(
        Guid id,
        Guid tenantId,
        EmailAddress email,
        string roleCode,
        string tokenHash,
        ExternalUserId invitedBy,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invitation ID cannot be empty.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(invitedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Invitation lifetime must be positive.", nameof(lifetime));
        }

        return new Invitation
        {
            Id = id,
            TenantId = tenantId,
            Email = email,
            RoleCode = roleCode,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            InvitedByExternalUserId = invitedBy,
            CreatedAt = createdAt,
            ExpiresAt = createdAt + lifetime
        };
    }

    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;

    public void Accept(ExternalUserId acceptedBy, DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(acceptedBy);
        RequirePending();

        Status = InvitationStatus.Accepted;
        AcceptedByExternalUserId = acceptedBy;
        AcceptedAt = acceptedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RequirePending();

        Status = InvitationStatus.Revoked;
        AcceptedAt = null;
    }

    private void RequirePending()
    {
        if (Status is not InvitationStatus.Pending)
        {
            throw new InvalidOperationException($"Invitation is {Status}, not Pending.");
        }
    }
}
```

- [ ] **Step 5: `InvitationTokens`'ı yaz**

`apps/api/src/Infrastructure/Access/InvitationTokens.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Access;

public static class InvitationTokens
{
    private const int ByteLength = 32;

    public static string Create() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
```

`Base64Url` .NET 9'dan beri `System.Buffers.Text` altında bulunuyor; derleme hatası verirse
`Convert.ToBase64String` çıktısındaki `+`, `/` ve `=` karakterlerini elle değiştir.

- [ ] **Step 6: EF yapılandırmasını yaz**

`apps/api/src/Infrastructure/Persistence/ControlPlane/Configurations/InvitationConfiguration.cs`:

```csharp
using Domain.Access;
using Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.ControlPlane.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value))
            .IsRequired();
        builder.Property(x => x.RoleCode).HasColumnName("role_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.InvitedByExternalUserId)
            .HasColumnName("invited_by_external_user_id")
            .HasMaxLength(255)
            .HasConversion(userId => userId.Value, value => ExternalUserId.Create(value))
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.AcceptedByExternalUserId)
            .HasColumnName("accepted_by_external_user_id")
            .HasMaxLength(255)
            .HasConversion(
                userId => userId!.Value,
                value => ExternalUserId.Create(value));
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Email })
            .IsUnique()
            .HasFilter("status = 'Pending'");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 7: `DbSet`'i ekle ve migration üret**

`ControlPlaneDbContext` içine ekle:

```csharp
    public DbSet<Invitation> Invitations => Set<Invitation>();
```

```bash
cd apps/api
dotnet tool run dotnet-ef migrations add AddInvitations \
  --project src/Infrastructure --startup-project src/Api \
  --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations
```

- [ ] **Step 8: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/UnitTests/UnitTests.csproj`
Expected: `EmailAddressTests` ve `InvitationTests` PASS.

- [ ] **Step 9: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): record tenant invitations with hashed tokens"
```

---

### Görev 5: Davet oluşturma, listeleme ve iptal

**Files:**
- Create: `apps/api/src/Api/Features/Invitations/InvitationEndpoints.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/InvitationEndpointTests.cs`

**Interfaces:**
- Consumes: Görev 3'ün `RequirePermission`'ı ve `TenantSurfaceFixture`'ı, Görev 4'ün
  `Invitation` ve `InvitationTokens`'ı.
- Produces: `IEndpointRouteBuilder.MapTenantInvitations()`;
  `POST|GET /{tenant-alias}/api/v1/invitations`, `DELETE .../invitations/{id}`.

Kritik disiplin: her satırın `tenant_id`'si `TenantContext.TenantId`'den gelir, istekten
gelen hiçbir tanımlayıcıdan değil. Tenant yüzeyi control plane'e yazabildiği için tenant'lar
arası sızmayı engelleyen tek şey budur.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/InvitationEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationEndpointTests
{
    private static string InvitationsUrl => $"/{TenantSurfaceFixture.Alias}/api/v1/invitations";

    [Fact]
    public async Task PostInvitation_ReturnsTheTokenOnceAndStoresOnlyItsHash()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "Invited@Example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = body.GetProperty("token").GetString()!;
        Assert.Equal("invited@example.com", body.GetProperty("email").GetString());
        await using var context = fixture.CreateControlPlane();
        var stored = await context.Invitations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, stored.TokenHash, StringComparison.Ordinal);
        Assert.Equal(InvitationStatus.Pending, stored.Status);
        Assert.Equal(fixture.Tenant.Id, stored.TenantId);
    }

    [Fact]
    public async Task PostInvitation_ForAnUnknownRole_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "sorcerer" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostInvitation_ForAnInvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "not-an-email", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostInvitation_ForADuplicatePendingEmail_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        using var first = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "INVITED@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetInvitations_ListsOnlyPendingOnes()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        using var created = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();

        // Act
        using var listed = await fixture.Client.GetAsync(InvitationsUrl, TestContext.Current.CancellationToken);
        using var revoked = await fixture.Client.DeleteAsync(
            $"{InvitationsUrl}/{id}",
            TestContext.Current.CancellationToken);
        using var listedAfter = await fixture.Client.GetAsync(InvitationsUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Single(await listed.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken)!);
        Assert.Empty(await listedAfter.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken)!);
    }

    [Fact]
    public async Task DeleteInvitation_ForAnUnknownIdentifier_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{InvitationsUrl}/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: Endpoint'ler yok, testler 404 alır.

- [ ] **Step 3: Endpoint'leri yaz**

`apps/api/src/Api/Features/Invitations/InvitationEndpoints.cs`:

```csharp
using System.Security.Claims;
using Api.Authorization;
using Api.Tenants;
using Domain.Access;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Invitations;

public sealed record CreateInvitationRequest(string Email, string RoleCode);

public sealed record CreatedInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt,
    string Token);

public sealed record PendingInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public static class InvitationEndpoints
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapTenantInvitations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/invitations");

        group.MapPost("/", CreateAsync).RequirePermission(TenantPermissions.InvitationsManage);
        group.MapGet("/", ListAsync).RequirePermission(TenantPermissions.InvitationsManage);
        group.MapDelete("/{id:guid}", RevokeAsync).RequirePermission(TenantPermissions.InvitationsManage);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateInvitationRequest request,
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        TenantDbContext tenantDbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        EmailAddress email;

        try
        {
            email = EmailAddress.Create(request.Email);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        var roleExists = await tenantDbContext.Roles
            .AnyAsync(role => role.Code == request.RoleCode, cancellationToken);

        if (!roleExists)
        {
            return Results.BadRequest(new { error = $"Role '{request.RoleCode}' does not exist." });
        }

        var alreadyInvited = await controlPlaneDbContext.Invitations.AnyAsync(
            invitation => invitation.TenantId == tenantContext.TenantId
                          && invitation.Email == email
                          && invitation.Status == InvitationStatus.Pending,
            cancellationToken);

        if (alreadyInvited)
        {
            return Results.Conflict(new { error = $"'{email.Value}' already has a pending invitation." });
        }

        var token = InvitationTokens.Create();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(),
            tenantContext.TenantId,
            email,
            request.RoleCode,
            InvitationTokens.Hash(token),
            ExternalUserId.Create(user.FindFirstValue("sub")!),
            timeProvider.GetUtcNow(),
            Lifetime);
        controlPlaneDbContext.Invitations.Add(invitation);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/{tenantContext.Alias}/api/v1/invitations/{invitation.Id}",
            new CreatedInvitation(
                invitation.Id,
                invitation.Email.Value,
                invitation.RoleCode,
                invitation.ExpiresAt,
                token));
    }

    private static async Task<IResult> ListAsync(
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        CancellationToken cancellationToken)
    {
        var invitations = await controlPlaneDbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantContext.TenantId
                                 && invitation.Status == InvitationStatus.Pending)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new PendingInvitation(
                invitation.Id,
                invitation.Email.Value,
                invitation.RoleCode,
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(invitations);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invitation = await controlPlaneDbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.Id == id
                         && candidate.TenantId == tenantContext.TenantId
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Results.NotFound();
        }

        invitation.Revoke(timeProvider.GetUtcNow());
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
```

- [ ] **Step 4: Permission kodlarını sabitle**

`apps/api/src/Api/Authorization/TenantClaims.cs` dosyasına ekle:

```csharp
public static class TenantPermissions
{
    public const string MembersRead = "members.read";

    public const string MembersManage = "members.manage";

    public const string RolesRead = "roles.read";

    public const string RolesManage = "roles.manage";

    public const string InvitationsManage = "invitations.manage";
}
```

- [ ] **Step 5: Grubu bağla**

`apps/api/src/Api/Program.cs` içine, `app.MapControlPlane();` satırından sonra ekle:

```csharp
app.MapTenantInvitations();
```

`Program.cs` başına `using Api.Features.Invitations;` ekle.

- [ ] **Step 6: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `InvitationEndpointTests` içindeki altı test PASS.

- [ ] **Step 7: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): invite people to a tenant"
```

---

### Görev 6: Kullanıcı yüzeyi — kendi tenant'larım, davetlerim ve kabul

**Files:**
- Create: `apps/api/src/Api/Features/Me/MeEndpoints.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/InvitationAcceptanceTests.cs`

**Interfaces:**
- Consumes: Görev 4'ün `Invitation` ve `InvitationTokens`'ı.
- Produces: `IEndpointRouteBuilder.MapMe()`; `GET /api/v1/memberships`,
  `GET /api/v1/invitations`, `POST /api/v1/invitations/accept`.

Bu yüzey yalnızca kimlik doğrulama ister; `TenantAccessMiddleware` route'ta `tenantAlias`
bulunmadığı için devreye girmez ve `api` alias'ı rezerve olduğu için hiçbir tenant bu yolu
gölgeleyemez.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/InvitationAcceptanceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Access;
using Domain.Access.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationAcceptanceTests
{
    private const string InvitedUserId = "user_invited";
    private const string InvitedEmail = "invited@example.com";

    [Fact]
    public async Task AcceptInvitation_LetsTheInvitedPersonUseTheTenant()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var invited = await owner.WithPrincipalAsync(InvitedUserId, InvitedEmail);

        // Act
        using var accepted = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var members = await invited.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        await using var tenantDbContext = owner.CreateTenantDbContext();
        var user = await tenantDbContext.Users.SingleAsync(
            candidate => candidate.ExternalUserId == ExternalUserId.Create(InvitedUserId),
            TestContext.Current.CancellationToken);
        Assert.Equal(TenantUserStatus.Active, user.Status);
    }

    [Fact]
    public async Task AcceptInvitation_WithAnUnknownToken_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            InvitedUserId,
            InvitedEmail,
            roleCode: null);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token = "not-a-real-token" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WithAMismatchedEmail_ReturnsForbidden()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var stranger = await owner.WithPrincipalAsync("user_stranger", "stranger@example.com");

        // Act
        using var response = await stranger.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WithoutAnEmailClaim_ReturnsForbidden()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var anonymousEmail = await owner.WithPrincipalAsync(InvitedUserId, email: null);

        // Act
        using var response = await anonymousEmail.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_Twice_ReturnsNotFound()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var invited = await owner.WithPrincipalAsync(InvitedUserId, InvitedEmail);
        using var first = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using var response = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_AfterItExpired_ReturnsGone()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");

        await using (var context = owner.CreateControlPlane())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE control.invitations SET expires_at = now() - interval '1 day'",
                TestContext.Current.CancellationToken);
        }

        await using var invited = await owner.WithPrincipalAsync(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task GetMyInvitations_ListsOnlyMyPendingOnes()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        await InviteAsync(owner, InvitedEmail, "member");
        await InviteAsync(owner, "someone.else@example.com", "member");
        await using var invited = await owner.WithPrincipalAsync(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.GetAsync(
            "/api/v1/invitations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var only = Assert.Single(body!);
        Assert.Equal(InvitedEmail, only.GetProperty("email").GetString());
        Assert.Equal(TenantSurfaceFixture.Alias, only.GetProperty("tenantAlias").GetString());
    }

    [Fact]
    public async Task GetMyMemberships_ListsOnlyTenantsIBelongTo()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        await using var stranger = await owner.WithPrincipalAsync("user_stranger", "stranger@example.com");

        // Act
        using var mine = await owner.Client.GetAsync("/api/v1/memberships", TestContext.Current.CancellationToken);
        using var theirs = await stranger.Client.GetAsync(
            "/api/v1/memberships",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(await mine.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken)!);
        Assert.Empty(await theirs.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken)!);
    }

    private static async Task<string> InviteAsync(
        TenantSurfaceFixture fixture,
        string email,
        string roleCode)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email, roleCode },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        return body.GetProperty("token").GetString()!;
    }
}
```

- [ ] **Step 2: Fixture'a ikinci bir kimlik açma yeteneği ekle**

Kabul akışı, aynı veritabanına farklı bir kullanıcı kimliğiyle bağlanan ikinci bir HTTP
istemcisi gerektirir. `TenantSurfaceFixture` sınıfına ekle:

```csharp
    public Task<TenantSurfaceFixture> WithPrincipalAsync(string externalUserId, string? email) =>
        Task.FromResult(new TenantSurfaceFixture(
            postgres,
            BuildFactory(ControlPlaneConnectionString, ServerConnectionString, externalUserId, email),
            Tenant,
            ControlPlaneConnectionString,
            ownsContainer: false));
```

Kurucuya `bool ownsContainer` parametresi eklenir, alan olarak saklanır ve `DisposeAsync`
yalnızca `ownsContainer` doğruysa container'ı atar:

```csharp
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();

        if (ownsContainer)
        {
            await postgres.DisposeAsync();
        }
    }
```

`StartAsync` içindeki `WebApplicationFactory` kurulumu, yeniden kullanılabilmesi için
`BuildFactory(string controlPlane, string connectionString, string externalUserId, string? email)`
adlı private static metoda taşınır ve `StartAsync` bu metodu çağırır.

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `/api/v1/...` endpoint'leri yok, testler 404 alır.

- [ ] **Step 4: Endpoint'leri yaz**

`apps/api/src/Api/Features/Me/MeEndpoints.cs`:

```csharp
using System.Security.Claims;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Me;

public sealed record AcceptInvitationRequest(string Token);

public sealed record MyMembership(Guid TenantId, string TenantAlias, string TenantStatus);

public sealed record MyInvitation(
    Guid Id,
    string TenantAlias,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").RequireAuthorization();

        group.MapGet("/memberships", ListMembershipsAsync);
        group.MapGet("/invitations", ListInvitationsAsync);
        group.MapPost("/invitations/accept", AcceptAsync);

        return endpoints;
    }

    private static async Task<IResult> ListMembershipsAsync(
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(user.FindFirstValue("sub")!);
        var memberships = await dbContext.Memberships
            .AsNoTracking()
            .Where(membership => membership.ExternalUserId == externalUserId)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (_, tenant) => new MyMembership(tenant.Id, tenant.Alias.Value, tenant.Status.ToString()))
            .OrderBy(membership => membership.TenantAlias)
            .ToListAsync(cancellationToken);

        return Results.Ok(memberships);
    }

    private static async Task<IResult> ListInvitationsAsync(
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmail(user, out var email))
        {
            return EmailClaimRequired();
        }

        var now = timeProvider.GetUtcNow();
        var invitations = await dbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.Email == email
                                 && invitation.Status == InvitationStatus.Pending
                                 && invitation.ExpiresAt >= now)
            .Join(
                dbContext.Tenants,
                invitation => invitation.TenantId,
                tenant => tenant.Id,
                (invitation, tenant) => new MyInvitation(
                    invitation.Id,
                    tenant.Alias.Value,
                    invitation.Email.Value,
                    invitation.RoleCode,
                    invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(invitations);
    }

    private static async Task<IResult> AcceptAsync(
        AcceptInvitationRequest request,
        ControlPlaneDbContext dbContext,
        TenantDbContextFactory tenantDbContextFactory,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Results.NotFound();
        }

        var tokenHash = InvitationTokens.Hash(request.Token);
        var invitation = await dbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();

        if (invitation.IsExpired(now))
        {
            return Results.StatusCode(StatusCodes.Status410Gone);
        }

        if (!TryGetEmail(user, out var email))
        {
            return EmailClaimRequired();
        }

        if (email != invitation.Email)
        {
            return Results.Forbid();
        }

        var tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == invitation.TenantId,
            cancellationToken);

        if (tenant.Status is not TenantStatus.Active)
        {
            return Results.Conflict(new { error = $"Tenant is {tenant.Status}, not Active." });
        }

        var externalUserId = ExternalUserId.Create(user.FindFirstValue("sub")!);
        await using var tenantDbContext = tenantDbContextFactory.Create(tenant.DatabaseName.Value);
        var role = await tenantDbContext.Roles.SingleOrDefaultAsync(
            candidate => candidate.Code == invitation.RoleCode,
            cancellationToken);

        if (role is null)
        {
            return Results.Conflict(new { error = $"Role '{invitation.RoleCode}' no longer exists." });
        }

        // The tenant database is written before the control plane so that a failure in between
        // leaves no membership, and therefore no way in, until the retry completes.
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalUserId == externalUserId,
            cancellationToken);

        if (tenantUser is null)
        {
            tenantUser = TenantUser.Create(Guid.CreateVersion7(), externalUserId, TenantUserStatus.Active);
            tenantDbContext.Users.Add(tenantUser);
        }
        else
        {
            tenantUser.Enable();
        }

        var hasRole = await tenantDbContext.UserRoles.AnyAsync(
            assignment => assignment.UserId == tenantUser.Id && assignment.RoleId == role.Id,
            cancellationToken);

        if (!hasRole)
        {
            tenantDbContext.UserRoles.Add(TenantUserRole.Create(tenantUser.Id, role.Id));
        }

        await tenantDbContext.SaveChangesAsync(cancellationToken);

        var hasMembership = await dbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id
                          && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            dbContext.Memberships.Add(Membership.Create(Guid.CreateVersion7(), tenant.Id, externalUserId));
        }

        invitation.Accept(externalUserId, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new MyMembership(tenant.Id, tenant.Alias.Value, tenant.Status.ToString()));
    }

    private static bool TryGetEmail(ClaimsPrincipal user, out EmailAddress email)
    {
        var claim = user.FindFirstValue("email");
        email = null!;

        if (string.IsNullOrWhiteSpace(claim))
        {
            return false;
        }

        try
        {
            email = EmailAddress.Create(claim);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IResult EmailClaimRequired() => Results.Problem(
        detail: "The access token must carry a verified 'email' claim. Add it to the Clerk JWT template.",
        statusCode: StatusCodes.Status403Forbidden);
}
```

- [ ] **Step 5: `TenantUser.Enable()` ve `Disable()`'ı ekle**

`apps/api/src/Domain/Access/Users/TenantUser.cs` sınıfına ekle:

```csharp
    public void Enable() => Status = TenantUserStatus.Active;

    public void Disable() => Status = TenantUserStatus.Disabled;
```

- [ ] **Step 6: Grubu bağla**

`apps/api/src/Api/Program.cs` içine, `app.MapTenantInvitations();` satırından sonra ekle:

```csharp
app.MapMe();
```

`Program.cs` başına `using Api.Features.Me;` ekle.

- [ ] **Step 7: Testlerin geçtiğini doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `InvitationAcceptanceTests` içindeki sekiz test PASS.

- [ ] **Step 8: Commit**

```bash
git add -A apps/api
git commit -m "feat(api): accept invitations and list a user's tenants"
```

---

### Görev 7: Üye listeleme, rol değiştirme, iptal ve devre dışı bırakma

**Files:**
- Create: `apps/api/src/Api/Features/Members/MemberEndpoints.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Modify: `apps/api/AGENTS.md`
- Test: `apps/api/tests/IntegrationTests/MemberEndpointTests.cs`

**Interfaces:**
- Consumes: Görev 3'ün `RequirePermission`'ı, Görev 6'nın `TenantUser.Enable/Disable`'ı.
- Produces: `IEndpointRouteBuilder.MapTenantMembers()`;
  `GET /{tenant-alias}/api/v1/members`, `PUT .../members/{externalUserId}/roles`,
  `DELETE .../members/{externalUserId}`, `POST .../members/{externalUserId}/disable|enable`.

- [ ] **Step 1: Başarısız testleri yaz**

`apps/api/tests/IntegrationTests/MemberEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Access;
using Domain.Access.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class MemberEndpointTests
{
    private const string InvitedUserId = "user_invited";
    private const string InvitedEmail = "invited@example.com";

    private static string MembersUrl => $"/{TenantSurfaceFixture.Alias}/api/v1/members";

    [Fact]
    public async Task GetMembers_ListsUsersWithTheirRoles()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var only = Assert.Single(body!);
        Assert.Equal(TenantSurfaceFixture.OwnerUserId, only.GetProperty("externalUserId").GetString());
        Assert.Equal("Active", only.GetProperty("status").GetString());
        Assert.Contains(
            "owner",
            only.GetProperty("roleCodes").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task DeleteMember_RevokesEntryAndDisablesTheUser()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{InvitedUserId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var control = fixture.CreateControlPlane();
        Assert.False(await control.Memberships.AnyAsync(
            membership => membership.ExternalUserId == ExternalUserId.Create(InvitedUserId),
            TestContext.Current.CancellationToken));
        await using var tenantDbContext = fixture.CreateTenantDbContext();
        var user = await tenantDbContext.Users.SingleAsync(
            candidate => candidate.ExternalUserId == ExternalUserId.Create(InvitedUserId),
            TestContext.Current.CancellationToken);
        Assert.Equal(TenantUserStatus.Disabled, user.Status);
        Assert.False(await tenantDbContext.UserRoles.AnyAsync(
            assignment => assignment.UserId == user.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteMember_AfterRevocation_TheTenantRejectsThem()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        using var revoked = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{InvitedUserId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await using var invited = await fixture.WithPrincipalAsync(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_ForTheLastOwner_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DisableMember_ForTheLastOwner_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}/disable",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ReplacesTheRolesAndChangesWhatTheMemberMayDo()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        await using var invited = await fixture.WithPrincipalAsync(InvitedUserId, InvitedEmail);
        using var beforeUpgrade = await invited.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = "third@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, beforeUpgrade.StatusCode);

        // Act
        using var upgraded = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{InvitedUserId}/roles",
            new { roleCodes = new[] { "owner" } },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, upgraded.StatusCode);
        using var afterUpgrade = await invited.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = "third@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, afterUpgrade.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ThatWouldDropTheLastOwner_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}/roles",
            new { roleCodes = new[] { "member" } },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ForAnUnknownRole_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();

        // Act
        using var response = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{InvitedUserId}/roles",
            new { roleCodes = new[] { "sorcerer" } },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisableThenEnable_RestoresAccess()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        await using var invited = await fixture.WithPrincipalAsync(InvitedUserId, InvitedEmail);

        // Act
        using var disabled = await fixture.Client.PostAsync(
            $"{MembersUrl}/{InvitedUserId}/disable",
            content: null,
            TestContext.Current.CancellationToken);
        using var whileDisabled = await invited.Client.GetAsync(
            MembersUrl,
            TestContext.Current.CancellationToken);
        using var enabled = await fixture.Client.PostAsync(
            $"{MembersUrl}/{InvitedUserId}/enable",
            content: null,
            TestContext.Current.CancellationToken);
        using var afterEnable = await invited.Client.GetAsync(
            MembersUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, whileDisabled.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterEnable.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_ForAnUnknownUser_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/user_nobody",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<TenantSurfaceFixture> AcceptedMemberAsync()
    {
        var fixture = await TenantSurfaceFixture.StartAsync();
        using var created = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = InvitedEmail, roleCode = "member" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = body.GetProperty("token").GetString()!;
        await using var invited = await fixture.WithPrincipalAsync(InvitedUserId, InvitedEmail);
        using var accepted = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        return fixture;
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test --project apps/api/tests/IntegrationTests/IntegrationTests.csproj`
Expected: `members` endpoint'leri yok, testler 404 alır.

- [ ] **Step 3: Endpoint'leri yaz**

`apps/api/src/Api/Features/Members/MemberEndpoints.cs`:

```csharp
using Api.Authorization;
using Api.Tenants;
using Domain.Access;
using Domain.Access.Users;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Members;

public sealed record ReplaceRolesRequest(string[] RoleCodes);

public sealed record TenantMember(
    string ExternalUserId,
    string Status,
    string[] RoleCodes);

public static class MemberEndpoints
{
    private const string OwnerRoleCode = "owner";

    public static IEndpointRouteBuilder MapTenantMembers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/members");

        group.MapGet("/", ListAsync).RequirePermission(TenantPermissions.MembersRead);
        group.MapPut("/{externalUserId}/roles", ReplaceRolesAsync)
            .RequirePermission(TenantPermissions.RolesManage);
        group.MapDelete("/{externalUserId}", RevokeAsync)
            .RequirePermission(TenantPermissions.MembersManage);
        group.MapPost("/{externalUserId}/disable", DisableAsync)
            .RequirePermission(TenantPermissions.MembersManage);
        group.MapPost("/{externalUserId}/enable", EnableAsync)
            .RequirePermission(TenantPermissions.MembersManage);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var users = await tenantDbContext.Users.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await tenantDbContext.UserRoles
            .AsNoTracking()
            .Join(
                tenantDbContext.Roles,
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => new { assignment.UserId, role.Code })
            .ToListAsync(cancellationToken);
        var members = users
            .Select(user => new TenantMember(
                user.ExternalUserId.Value,
                user.Status.ToString(),
                assignments
                    .Where(assignment => assignment.UserId == user.Id)
                    .Select(assignment => assignment.Code!)
                    .OrderBy(code => code)
                    .ToArray()))
            .OrderBy(member => member.ExternalUserId)
            .ToList();

        return Results.Ok(members);
    }

    private static async Task<IResult> ReplaceRolesAsync(
        string externalUserId,
        ReplaceRolesRequest request,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = await tenantDbContext.Roles
            .Where(role => request.RoleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);

        if (roles.Count != request.RoleCodes.Distinct().Count())
        {
            return Results.BadRequest(new { error = "One or more roles do not exist." });
        }

        if (!roles.Any(role => role.Code == OwnerRoleCode)
            && await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        var existing = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);
        tenantDbContext.UserRoles.RemoveRange(existing);
        tenantDbContext.UserRoles.AddRange(
            roles.Select(role => TenantUserRole.Create(user.Id, role.Id)));
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAsync(
        string externalUserId,
        TenantContext tenantContext,
        TenantDbContext tenantDbContext,
        ControlPlaneDbContext controlPlaneDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        if (await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        var userId = ExternalUserId.Create(externalUserId);
        var memberships = await controlPlaneDbContext.Memberships
            .Where(membership => membership.TenantId == tenantContext.TenantId
                                 && membership.ExternalUserId == userId)
            .ToListAsync(cancellationToken);
        controlPlaneDbContext.Memberships.RemoveRange(memberships);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        var assignments = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);
        tenantDbContext.UserRoles.RemoveRange(assignments);
        user.Disable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(
        string externalUserId,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        if (await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        user.Disable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> EnableAsync(
        string externalUserId,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        user.Enable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static Task<TenantUser?> FindUserAsync(
        TenantDbContext tenantDbContext,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var userId = ExternalUserId.Create(externalUserId);

        return tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            cancellationToken);
    }

    private static async Task<bool> IsLastOwnerAsync(
        TenantDbContext tenantDbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ownerUserIds = await tenantDbContext.UserRoles
            .Join(
                tenantDbContext.Roles.Where(role => role.Code == OwnerRoleCode),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, _) => assignment.UserId)
            .Join(
                tenantDbContext.Users.Where(user => user.Status == TenantUserStatus.Active),
                ownerId => ownerId,
                user => user.Id,
                (ownerId, _) => ownerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ownerUserIds.Count == 1 && ownerUserIds[0] == userId;
    }

    private static IResult LastOwner() => Results.Conflict(new
    {
        error = "The last owner of a tenant cannot be removed, disabled or demoted."
    });
}
```

- [ ] **Step 4: Grubu bağla**

`apps/api/src/Api/Program.cs` içine, `app.MapMe();` satırından sonra ekle:

```csharp
app.MapTenantMembers();
```

`Program.cs` başına `using Api.Features.Members;` ekle.

- [ ] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --solution apps/api/Api.slnx`
Expected: Görev 3'te yazılan `PermissionEnforcementTests` dahil tüm testler PASS.

- [ ] **Step 6: `AGENTS.md`'yi güncelle**

`apps/api/AGENTS.md` içindeki control plane kuralını şununla değiştir:

```markdown
- Resolve tenants and check membership through the read-only control plane credential. Tenant-surface endpoints that write control plane rows must scope every row to the resolved tenant, never to an identifier taken from the request.
```

Kurallar listesine ekle:

```markdown
- Require an `email` claim on the access token for invitation acceptance; possession of an invitation token alone must not grant entry. The Clerk JWT template must include `email`.
- Store only the hash of an invitation token; return the plaintext once, at creation.
- Refuse to remove, disable or demote the last owner of a tenant.
```

- [ ] **Step 7: OpenAPI istemcisini yeniden üret**

```bash
cd apps/api && dotnet build src/Api/Api.csproj
pnpm --filter @st/api-client build
```

- [ ] **Step 8: Commit**

```bash
git add -A apps/api packages/api-client
git commit -m "feat(api): manage tenant members and their roles"
```

---

### Görev 8: Rol listeleme

**Files:**
- Create: `apps/api/src/Api/Features/Roles/RoleEndpoints.cs`
- Modify: `apps/api/src/Api/Program.cs`
- Test: `apps/api/tests/IntegrationTests/RoleEndpointTests.cs`

Bu görev plan ilk yazıldığında atlanmıştı: spec'in kapsam maddesi "Üye **ve rol** listeleme"
diyor ve `roles.read` permission'ı katalogda tanımlıydı, ancak hiçbir endpoint onu
tüketmiyordu. Kapsam denetimi sırasında fark edildi ve kapatıldı.

- [x] `GET /{tenant-alias}/api/v1/roles`, `roles.read` gerektiriyor; rolleri permission
      kodlarıyla listeliyor
- [x] Projeksiyon materyalizasyondan sonra yapılıyor — Görev 6'da öğrenilen EF kısıtı
- [x] Testler: sistem rolleri ve permission'ları doğru listeleniyor; `member` rolündeki
      kullanıcı erişebiliyor; tüm rolleri kaldırılan kullanıcı 403 alıyor

---

## Tamamlanma Kontrolü

2026-09-10 tarihinde doğrulandı:

- [x] `dotnet test --solution apps/api/Api.slnx` — dört proje, 154 test yeşil
- [x] Davet zincirinin tamamı entegrasyon testleriyle kanıtlandı: sahip davet eder, davet
      edilen kişi kabul eder ve tenant endpoint'ini çağırabilir; rolü `owner`'a yükseltilince
      davet oluşturabilir; üyeliği geri alınınca 403 alır
- [x] Güvenlik davranışları kanıtlandı: bilinmeyen token 404, süresi geçmiş token 410,
      e-posta uyuşmazlığı 403, `email` claim'i olmayan istek 403, ikinci kabul 404
- [x] Son owner koruması üç yolda da çalışıyor: silme, devre dışı bırakma ve rol düşürme 409
- [x] Rol listeleme `roles.read` ile korunuyor; rolleri kaldırılan kullanıcı 403 alıyor
- [x] Temiz ortamda migration'lar uygulanıyor; `control` şeması `invitations` tablosunu
      içeriyor ve yeni tenant veritabanları `member` ile `owner` rollerinin ikisiyle de doğuyor
- [x] Uygulama Development ortamında ayağa kalkıyor, `/health/ready` 200 dönüyor, her iki yeni
      yüzey de kimlik doğrulaması istiyor
- [x] `grep -rn "systemdb\|AdminDbContext" apps/api --include='*.cs'` boş dönüyor
- [x] `apps/api/AGENTS.md` dört yeni kuralı içeriyor
- [x] `packages/api-client/src/schema.d.ts` on beş endpoint'i içeriyor

HTTP seviyesinde elle uçtan uca akış çalıştırılmadı çünkü tüm yüzeyler Clerk token'ı
gerektiriyor ve `Clerk:Issuer` lokalde yapılandırılmamış durumda. Aynı zincir entegrasyon
testlerinde test kimlik doğrulama şemasıyla uçtan uca yürütülüyor.
