# apps/api Phase 2b: Persistence Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Real EF Core + Npgsql persistence for the `admin` control-plane schema (Tenants/Users/Memberships/Invitations/RolePermissions), tenant schema provisioning, per-tenant migration application, a dual-ID sequence generator, optimistic concurrency, and UTC audit timestamps — all proven against a real Postgres via Testcontainers.

**Architecture:** `Api.Domain` gains 5 new entities + `IAuditable`, still BCL-only. `Api.Infrastructure` gains `AdminDbContext` (fixed `admin` schema), `TenantDbContext` (dynamic schema, currently zero entities — a shell for a future product), `TenantProvisioningService`, `TenantSequenceIdGenerator`, `TenantMigrationRunner`, `AuditableSaveChangesInterceptor`. A new `Api.Tests.Integration` project runs everything against a real, Testcontainers-managed Postgres.

**Tech Stack:** EF Core 10 (Npgsql provider), Testcontainers.PostgreSql, xUnit.

## Global Constraints

- No tenant-schema business entities are invented in this phase (see the phase spec's "Kapsam kararı" section) — `TenantDbContext` stays deliberately empty of `DbSet`s.
- Every `Api.Domain` entity keeps the private-constructor-plus-static-factory shape established in Phase 2a; EF Core materializes them via its own reflection-based construction binding (no public parameterless constructors needed beyond a `private` one for EF).
- `Email`/`TenantSlug` are mapped via `HasConversion` (not `OwnsOne`/`ComplexProperty`) — the simpler, long-stable EF Core pattern for a value object wrapping one primitive.
- `modelBuilder.Ignore<DomainEvent>()` must be called in every `DbContext`'s `OnModelCreating` — otherwise EF Core tries to map `AggregateRoot<TId>.DomainEvents` as a navigation to a keyless abstract type and throws at model-build time (this is Phase 2a's final-review finding #3, now actually paid off).
- Membership concurrency uses Npgsql's `UseXminAsConcurrencyToken()` (Postgres's native `xmin` system column) — not a hand-rolled `RowVersion` byte array, which is a SQL-Server-flavored pattern that doesn't fit Postgres.
- Any raw SQL that interpolates a schema/sequence name must first validate the identifier against a safe-character allowlist (defense in depth), even where the caller already went through `TenantSlug` validation.
- **API-detail allowance:** this task touches several less-common EF Core/Npgsql APIs (`HasConversion`, `UseXminAsConcurrencyToken`, `IModelCacheKeyFactory`, `ApplyConfigurationsFromAssembly`) written from documentation knowledge, not verified against the exact installed package version. If a specific call in this plan doesn't compile or behave as described, adjust to the nearest correct equivalent that preserves the same column/behavior contract (e.g. a different overload, an explicit `ValueConverter` class instead of the fluent lambda form), and document the deviation clearly in your report — the same latitude Phase 1 used for the `dotnet new sln` format surprise.
- Work from `/Users/sufyan/Documents/Projects/st` (repo root).
- Docker must be running (`docker info` succeeds) before any task with integration tests — confirm this before starting if unsure; it was already verified running at the start of this overnight session.

---

### Task 1: Integration test project and Postgres container fixture

**Files:**
- Create: `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
- Create: `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs`
- Create: `apps/api/tests/Api.Tests.Integration/PostgresConnectivityTests.cs`
- Modify: `apps/api/Api.sln` (add the new project)
- Modify: `apps/api/src/Api.Infrastructure/Api.Infrastructure.csproj` (add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`)

**Interfaces:**
- Consumes: nothing new from earlier phases besides the existing solution structure.
- Produces: `PostgresContainerFixture` (starts one shared Postgres 17 container for the whole integration-test collection, exposes `ConnectionString`), the `PostgresCollection` xUnit collection definition every later integration test class joins via `[Collection(nameof(PostgresCollection))]`. Later tasks extend `PostgresContainerFixture.InitializeAsync` to also run `AdminDbContext` migrations once the context exists (Task 3) — don't build that in yet, just the container lifecycle.

- [ ] **Step 1: Add Infrastructure's EF Core packages**

```bash
cd apps/api
dotnet add src/Api.Infrastructure/Api.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Api.Infrastructure/Api.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 2: Scaffold the integration test project**

```bash
dotnet new xunit -n Api.Tests.Integration -o tests/Api.Tests.Integration
rm tests/Api.Tests.Integration/UnitTest1.cs
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj reference src/Api.Infrastructure/Api.Infrastructure.csproj
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj package Testcontainers.PostgreSql
dotnet sln Api.sln add tests/Api.Tests.Integration/Api.Tests.Integration.csproj
```

- [ ] **Step 3: Write the container fixture**

Create `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs`:

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Tests.Integration;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
```

- [ ] **Step 4: Write the connectivity smoke test**

Create `apps/api/tests/Api.Tests.Integration/PostgresConnectivityTests.cs`:

```csharp
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class PostgresConnectivityTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Container_IsReachable_ExecutesSimpleQuery()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        // Act
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync();

        // Assert
        Assert.Equal(1, result);
    }
}
```

(`Npgsql` comes in transitively via `Npgsql.EntityFrameworkCore.PostgreSQL` referenced through `Api.Infrastructure` — if the compiler can't find the `Npgsql` namespace, add `dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj package Npgsql` directly.)

- [ ] **Step 5: Run the new test**

Run: `dotnet test tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0` (first run pulls the `postgres:17-alpine` image, may take a minute).

- [ ] **Step 6: Run the full solution test suite once**

Run: `dotnet test Api.sln`
Expected: `Failed: 0` overall (Phase 1 + Phase 2a's 43 tests from `Api.Tests.Unit`, plus this task's 1 new integration test — exact combined total depends on what's currently in `Api.Tests.Unit`; the only hard requirement is `Failed: 0`).

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add Api.Tests.Integration with Testcontainers Postgres fixture

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Admin schema domain entities

**Files:**
- Create: `apps/api/src/Api.Domain/IAuditable.cs`
- Create: `apps/api/src/Api.Domain/Tenant.cs`
- Create: `apps/api/src/Api.Domain/User.cs`
- Create: `apps/api/src/Api.Domain/Membership.cs`
- Create: `apps/api/src/Api.Domain/Invitation.cs`
- Create: `apps/api/src/Api.Domain/RolePermission.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/TenantTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/UserTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/MembershipTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/InvitationTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/RolePermissionTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `Entity<TId>`, `Email`, `TenantSlug` from Phase 2a.
- Produces: `Tenant.Create(TenantSlug, string name) : Tenant`, `Tenant.SchemaName` (computed, `"tenant_" + slug with '-' replaced by '_'`), `User.Create(string clerkUserId, Email) : User`, `User.SetTimeZone(string ianaId)`, `Membership.Create(Guid userId, Guid tenantId, string role) : Membership`, `Membership.ChangeRole(string role)`, `Invitation.Create(Guid tenantId, Email, string role) : Invitation` (generates its own `Token`), `RolePermission.Create(string role, string permission) : RolePermission`. Task 3's EF configurations map every property named here.

- [ ] **Step 1: Write the failing tests for `Tenant`**

Create `apps/api/tests/Api.Tests.Unit/Domain/TenantTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesAndDerivesSchemaName()
    {
        // Arrange
        var slug = TenantSlug.Create("acme-corp");

        // Act
        var tenant = Tenant.Create(slug, "Acme Corp");

        // Assert
        Assert.Equal(slug, tenant.Slug);
        Assert.Equal("Acme Corp", tenant.Name);
        Assert.Equal("tenant_acme_corp", tenant.SchemaName);
        Assert.NotEqual(Guid.Empty, tenant.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_ThrowsArgumentException(string name)
    {
        // Arrange
        var slug = TenantSlug.Create("acme");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Tenant.Create(slug, name));
    }
}
```

- [ ] **Step 2: Write the failing tests for `User`**

Create `apps/api/tests/Api.Tests.Unit/Domain/UserTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class UserTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesWithUtcDefaultTimeZone()
    {
        // Arrange
        var email = Email.Create("user@example.com");

        // Act
        var user = User.Create("clerk_abc123", email);

        // Assert
        Assert.Equal("clerk_abc123", user.ClerkUserId);
        Assert.Equal(email, user.Email);
        Assert.Equal("UTC", user.TimeZoneId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyClerkUserId_ThrowsArgumentException(string clerkUserId)
    {
        // Arrange
        var email = Email.Create("user@example.com");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => User.Create(clerkUserId, email));
    }

    [Fact]
    public void SetTimeZone_ValidInput_UpdatesTimeZoneId()
    {
        // Arrange
        var user = User.Create("clerk_abc123", Email.Create("user@example.com"));

        // Act
        user.SetTimeZone("Europe/Istanbul");

        // Assert
        Assert.Equal("Europe/Istanbul", user.TimeZoneId);
    }

    [Fact]
    public void SetTimeZone_EmptyInput_ThrowsArgumentException()
    {
        // Arrange
        var user = User.Create("clerk_abc123", Email.Create("user@example.com"));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => user.SetTimeZone(""));
    }
}
```

- [ ] **Step 3: Write the failing tests for `Membership`**

Create `apps/api/tests/Api.Tests.Unit/Domain/MembershipTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class MembershipTests
{
    [Fact]
    public void Create_ValidInput_SetsProperties()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Act
        var membership = Membership.Create(userId, tenantId, "admin");

        // Assert
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal("admin", membership.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyRole_ThrowsArgumentException(string role)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role));
    }

    [Fact]
    public void ChangeRole_ValidInput_UpdatesRole()
    {
        // Arrange
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "member");

        // Act
        membership.ChangeRole("admin");

        // Assert
        Assert.Equal("admin", membership.Role);
    }
}
```

- [ ] **Step 4: Write the failing tests for `Invitation`**

Create `apps/api/tests/Api.Tests.Unit/Domain/InvitationTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class InvitationTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesAndGeneratesToken()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = Email.Create("invitee@example.com");

        // Act
        var invitation = Invitation.Create(tenantId, email, "member");

        // Assert
        Assert.Equal(tenantId, invitation.TenantId);
        Assert.Equal(email, invitation.Email);
        Assert.Equal("member", invitation.Role);
        Assert.False(string.IsNullOrWhiteSpace(invitation.Token));
    }

    [Fact]
    public void Create_CalledTwice_GeneratesDifferentTokens()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = Email.Create("invitee@example.com");

        // Act
        var first = Invitation.Create(tenantId, email, "member");
        var second = Invitation.Create(tenantId, email, "member");

        // Assert
        Assert.NotEqual(first.Token, second.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyRole_ThrowsArgumentException(string role)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => Invitation.Create(Guid.NewGuid(), Email.Create("invitee@example.com"), role));
    }
}
```

- [ ] **Step 5: Write the failing tests for `RolePermission`**

Create `apps/api/tests/Api.Tests.Unit/Domain/RolePermissionTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class RolePermissionTests
{
    [Fact]
    public void Create_ValidInput_SetsProperties()
    {
        // Act
        var rolePermission = RolePermission.Create("admin", "tenant.invite");

        // Assert
        Assert.Equal("admin", rolePermission.Role);
        Assert.Equal("tenant.invite", rolePermission.Permission);
    }

    [Theory]
    [InlineData("", "tenant.invite")]
    [InlineData("admin", "")]
    public void Create_EmptyRoleOrPermission_ThrowsArgumentException(string role, string permission)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => RolePermission.Create(role, permission));
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~Domain.TenantTests|FullyQualifiedName~Domain.UserTests|FullyQualifiedName~Domain.MembershipTests|FullyQualifiedName~Domain.InvitationTests|FullyQualifiedName~Domain.RolePermissionTests"`
Expected: build failure — none of the 5 entity types exist yet.

- [ ] **Step 7: Implement `IAuditable`**

Create `apps/api/src/Api.Domain/IAuditable.cs`:

```csharp
namespace Api.Domain;

public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; }

    DateTimeOffset UpdatedAtUtc { get; }
}
```

- [ ] **Step 8: Implement `Tenant`**

Create `apps/api/src/Api.Domain/Tenant.cs`:

```csharp
namespace Api.Domain;

public sealed class Tenant : AggregateRoot<Guid>, IAuditable
{
    public TenantSlug Slug { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string SchemaName => $"tenant_{Slug.Value.Replace('-', '_')}";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Tenant()
    {
    }

    private Tenant(Guid id, TenantSlug slug, string name) : base(id)
    {
        Slug = slug;
        Name = name;
    }

    public static Tenant Create(TenantSlug slug, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name cannot be empty.", nameof(name));
        }

        return new Tenant(Guid.NewGuid(), slug, name);
    }
}
```

- [ ] **Step 9: Implement `User`**

Create `apps/api/src/Api.Domain/User.cs`:

```csharp
namespace Api.Domain;

public sealed class User : AggregateRoot<Guid>, IAuditable
{
    public string ClerkUserId { get; private set; } = null!;

    public Email Email { get; private set; } = null!;

    public string TimeZoneId { get; private set; } = "UTC";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private User()
    {
    }

    private User(Guid id, string clerkUserId, Email email) : base(id)
    {
        ClerkUserId = clerkUserId;
        Email = email;
    }

    public static User Create(string clerkUserId, Email email)
    {
        if (string.IsNullOrWhiteSpace(clerkUserId))
        {
            throw new ArgumentException("Clerk user id cannot be empty.", nameof(clerkUserId));
        }

        return new User(Guid.NewGuid(), clerkUserId, email);
    }

    public void SetTimeZone(string ianaTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZoneId))
        {
            throw new ArgumentException("Timezone id cannot be empty.", nameof(ianaTimeZoneId));
        }

        TimeZoneId = ianaTimeZoneId;
    }
}
```

- [ ] **Step 10: Implement `Membership`**

Create `apps/api/src/Api.Domain/Membership.cs`:

```csharp
namespace Api.Domain;

public sealed class Membership : AggregateRoot<Guid>, IAuditable
{
    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    public string Role { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Membership()
    {
    }

    private Membership(Guid id, Guid userId, Guid tenantId, string role) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        Role = role;
    }

    public static Membership Create(Guid userId, Guid tenantId, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        return new Membership(Guid.NewGuid(), userId, tenantId, role);
    }

    public void ChangeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        Role = role;
    }
}
```

- [ ] **Step 11: Implement `Invitation`**

Create `apps/api/src/Api.Domain/Invitation.cs`:

```csharp
using System.Security.Cryptography;

namespace Api.Domain;

public sealed class Invitation : AggregateRoot<Guid>, IAuditable
{
    public Guid TenantId { get; private set; }

    public Email Email { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public string Token { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Invitation()
    {
    }

    private Invitation(Guid id, Guid tenantId, Email email, string role, string token) : base(id)
    {
        TenantId = tenantId;
        Email = email;
        Role = role;
        Token = token;
    }

    public static Invitation Create(Guid tenantId, Email email, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new Invitation(Guid.NewGuid(), tenantId, email, role, token);
    }
}
```

- [ ] **Step 12: Implement `RolePermission`**

Create `apps/api/src/Api.Domain/RolePermission.cs`:

```csharp
namespace Api.Domain;

public sealed class RolePermission : Entity<Guid>
{
    public string Role { get; private set; } = null!;

    public string Permission { get; private set; } = null!;

    private RolePermission()
    {
    }

    private RolePermission(Guid id, string role, string permission) : base(id)
    {
        Role = role;
        Permission = permission;
    }

    public static RolePermission Create(string role, string permission)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission cannot be empty.", nameof(permission));
        }

        return new RolePermission(Guid.NewGuid(), role, permission);
    }
}
```

- [ ] **Step 13: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
Expected: `Failed: 0` — all previous 43 tests plus this task's new ones (2 Tenant + 4 User + 3 Membership + 3 Invitation + 2 RolePermission = 14 new; total 57). If the actual total differs, recount from the real output rather than accepting a mismatch silently.

- [ ] **Step 14: Commit**

```bash
git add apps/api/src/Api.Domain/IAuditable.cs apps/api/src/Api.Domain/Tenant.cs apps/api/src/Api.Domain/User.cs apps/api/src/Api.Domain/Membership.cs apps/api/src/Api.Domain/Invitation.cs apps/api/src/Api.Domain/RolePermission.cs apps/api/tests/Api.Tests.Unit/Domain/TenantTests.cs apps/api/tests/Api.Tests.Unit/Domain/UserTests.cs apps/api/tests/Api.Tests.Unit/Domain/MembershipTests.cs apps/api/tests/Api.Tests.Unit/Domain/InvitationTests.cs apps/api/tests/Api.Tests.Unit/Domain/RolePermissionTests.cs
git commit -m "feat(api): add admin schema domain entities (Tenant, User, Membership, Invitation, RolePermission)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `AdminDbContext`, EF configurations, audit interceptor, migration

**Files:**
- Create: `apps/api/src/Api.Infrastructure/AdminDbContext.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/TenantConfiguration.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/UserConfiguration.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/MembershipConfiguration.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/InvitationConfiguration.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/RolePermissionConfiguration.cs`
- Create: `apps/api/src/Api.Infrastructure/AuditableSaveChangesInterceptor.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (register `AdminDbContext` in DI)
- Modify: `apps/api/src/Api.Host/appsettings.json` (add `ConnectionStrings:AdminDb` placeholder)
- Modify: `apps/api/src/Api.Host/Api.Host.csproj` (add `Npgsql.EntityFrameworkCore.PostgreSQL` — needed for `UseNpgsql` at the composition root)
- Create: `apps/api/src/Api.Infrastructure/Migrations/` (EF Core migration files, generated by `dotnet ef`)
- Modify: `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs` (run the `AdminDbContext` migration once in `InitializeAsync`)
- Create: `apps/api/tests/Api.Tests.Integration/AdminDbContextTests.cs`

**Interfaces:**
- Consumes: the 5 domain entities and `IAuditable` from Task 2; `PostgresContainerFixture`/`PostgresCollection` from Task 1.
- Produces: `AdminDbContext` (5 `DbSet<T>` properties, fixed `"admin"` schema), `AuditableSaveChangesInterceptor` (used by Task 6's tests), a migration that creates `admin.Tenants`, `admin.Users`, `admin.Memberships`, `admin.Invitations`, `admin.RolePermissions`. Task 4 and Task 5 both take an `AdminDbContext` (or its `DbContextOptions<AdminDbContext>`) as a constructor dependency.

- [ ] **Step 1: Implement `AdminDbContext`**

Create `apps/api/src/Api.Infrastructure/AdminDbContext.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("admin");
        modelBuilder.Ignore<DomainEvent>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdminDbContext).Assembly);
    }
}
```

- [ ] **Step 2: Implement the entity configurations**

Create `apps/api/src/Api.Infrastructure/Configurations/TenantConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Slug)
            .HasConversion(slug => slug.Value, value => TenantSlug.Create(value))
            .HasColumnName("Slug")
            .HasMaxLength(63)
            .IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique();

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Ignore(t => t.SchemaName);
    }
}
```

Create `apps/api/src/Api.Infrastructure/Configurations/UserConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ClerkUserId)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(u => u.ClerkUserId).IsUnique();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => Email.Create(value))
            .HasColumnName("Email")
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();
    }
}
```

Create `apps/api/src/Api.Infrastructure/Configurations/MembershipConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();

        builder.UseXminAsConcurrencyToken();
    }
}
```

Create `apps/api/src/Api.Infrastructure/Configurations/InvitationConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email)
            .HasConversion(email => email.Value, value => Email.Create(value))
            .HasColumnName("Email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(i => i.Role)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Token)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(i => i.Token).IsUnique();
    }
}
```

Create `apps/api/src/Api.Infrastructure/Configurations/RolePermissionConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.Role).HasMaxLength(100).IsRequired();
        builder.Property(rp => rp.Permission).HasMaxLength(100).IsRequired();

        builder.HasIndex(rp => new { rp.Role, rp.Permission }).IsUnique();
    }
}
```

- [ ] **Step 3: Implement the audit interceptor**

Create `apps/api/src/Api.Infrastructure/AuditableSaveChangesInterceptor.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Api.Infrastructure;

public sealed class AuditableSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateAuditableEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditable.CreatedAtUtc)).CurrentValue = utcNow;
                entry.Property(nameof(IAuditable.UpdatedAtUtc)).CurrentValue = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(IAuditable.UpdatedAtUtc)).CurrentValue = utcNow;
            }
        }
    }
}
```

- [ ] **Step 4: Wire `AdminDbContext` into Api.Host's DI container**

```bash
cd apps/api
dotnet add src/Api.Host/Api.Host.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

In `apps/api/src/Api.Host/appsettings.json`, add a `ConnectionStrings` section (create the file's top-level object if it doesn't already have one; merge with existing content, don't replace it):

```json
{
  "ConnectionStrings": {
    "AdminDb": "Host=localhost;Database=api;Username=postgres;Password=postgres"
  }
}
```

In `apps/api/src/Api.Host/Program.cs`, add the `AdminDbContext` registration before `var app = builder.Build();`:

```csharp
builder.Services.AddDbContext<Api.Infrastructure.AdminDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AdminDb"))
        .AddInterceptors(new Api.Infrastructure.AuditableSaveChangesInterceptor()));
```

(Keep the existing `/health` `MapGet` and `public partial class Program;` lines exactly as they are — only insert the registration line above.)

- [ ] **Step 5: Verify the host still builds and the health test still passes**

Run: `dotnet build apps/api/Api.sln`
Expected: `Build succeeded.`

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0` (registering a `DbContext` that's never queried doesn't require a live database connection at host startup).

- [ ] **Step 6: Generate the initial migration**

```bash
cd apps/api
dotnet tool install --global dotnet-ef --version 10.* || dotnet tool update --global dotnet-ef --version 10.*
dotnet ef migrations add InitialAdminSchema --project src/Api.Infrastructure --startup-project src/Api.Host --context AdminDbContext --output-dir Migrations
```

If `dotnet ef` reports it cannot resolve a design-time `DbContext` because `Api.Host`'s `Program.cs` requires a real database connection at startup for something other than `AdminDbContext` itself — it shouldn't, since nothing else touches the DB — but if you hit this, implement `IDesignTimeDbContextFactory<AdminDbContext>` in `Api.Infrastructure` instead, using the same hardcoded local connection string from `appsettings.json`, and retry the command with `--project src/Api.Infrastructure` only (drop `--startup-project`).

- [ ] **Step 7: Extend the container fixture to apply the migration once**

Modify `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs` — replace the body of `InitializeAsync` with:

```csharp
public async Task InitializeAsync()
{
    await _container.StartAsync();

    var options = new DbContextOptionsBuilder<AdminDbContext>()
        .UseNpgsql(ConnectionString)
        .Options;

    await using var context = new AdminDbContext(options);
    await context.Database.MigrateAsync();
}
```

Add `using Api.Infrastructure;` and `using Microsoft.EntityFrameworkCore;` to the top of the file.

- [ ] **Step 8: Write the integration test verifying the 5 tables exist**

Create `apps/api/tests/Api.Tests.Integration/AdminDbContextTests.cs`:

```csharp
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AdminDbContextTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task Migration_CreatesAllFiveAdminTables()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        // Act
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'admin' ORDER BY table_name",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tableNames = new List<string>();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        // Assert
        Assert.Contains("Tenants", tableNames);
        Assert.Contains("Users", tableNames);
        Assert.Contains("Memberships", tableNames);
        Assert.Contains("Invitations", tableNames);
        Assert.Contains("RolePermissions", tableNames);
    }

    [Fact]
    public async Task AddTenant_PersistsAndReloads()
    {
        // Arrange
        var slug = Api.Domain.TenantSlug.Create($"test-{Guid.NewGuid():N}"[..20]);
        var tenant = Api.Domain.Tenant.Create(slug, "Test Tenant");

        // Act
        await using (var writeContext = CreateContext())
        {
            writeContext.Tenants.Add(tenant);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext();
        var reloaded = await readContext.Tenants.SingleAsync(t => t.Id == tenant.Id);

        // Assert
        Assert.Equal(slug, reloaded.Slug);
        Assert.Equal("Test Tenant", reloaded.Name);
    }
}
```

- [ ] **Step 9: Run the integration tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0` (the Task 1 connectivity test plus these 2 new ones).

- [ ] **Step 10: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 11: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add AdminDbContext, EF configurations, audit interceptor, initial migration

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Tenant provisioning and dual-ID sequence generator

**Files:**
- Create: `apps/api/src/Api.Infrastructure/TenantProvisioningService.cs`
- Create: `apps/api/src/Api.Infrastructure/TenantSequenceIdGenerator.cs`
- Create: `apps/api/tests/Api.Tests.Integration/TenantProvisioningServiceTests.cs`
- Create: `apps/api/tests/Api.Tests.Integration/TenantSequenceIdGeneratorTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`, `Tenant`, `TenantSlug` from Tasks 2-3.
- Produces: `TenantProvisioningService.ProvisionAsync(TenantSlug, string name) : Task<Tenant>` (creates the `Tenant` row AND the corresponding Postgres schema), `TenantSequenceIdGenerator.NextAsync(string schemaName, string sequenceName, string prefix) : Task<string>`. Task 5's `TenantMigrationRunner` tests will provision a tenant via this service before migrating its schema.

- [ ] **Step 1: Implement `TenantProvisioningService`**

Create `apps/api/src/Api.Infrastructure/TenantProvisioningService.cs`:

```csharp
using Api.Domain;

namespace Api.Infrastructure;

public sealed class TenantProvisioningService(AdminDbContext dbContext)
{
    public async Task<Tenant> ProvisionAsync(
        TenantSlug slug, string name, CancellationToken cancellationToken = default)
    {
        var tenant = Tenant.Create(slug, name);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        // tenant.SchemaName is derived from TenantSlug, whose Create()
        // already restricts input to lowercase letters/digits/hyphens
        // (Phase 2a) — safe to interpolate directly into DDL, it can
        // never contain quotes or SQL metacharacters.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{tenant.SchemaName}\"", cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return tenant;
    }
}
```

- [ ] **Step 2: Implement `TenantSequenceIdGenerator`**

Create `apps/api/src/Api.Infrastructure/TenantSequenceIdGenerator.cs`:

```csharp
using System.Text.RegularExpressions;
using Api.Domain;

namespace Api.Infrastructure;

public sealed partial class TenantSequenceIdGenerator(AdminDbContext dbContext)
{
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeIdentifierPattern();

    public async Task<string> NextAsync(
        string schemaName, string sequenceName, string prefix, CancellationToken cancellationToken = default)
    {
        EnsureSafeIdentifier(schemaName, nameof(schemaName));
        EnsureSafeIdentifier(sequenceName, nameof(sequenceName));

        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS \"{schemaName}\".\"{sequenceName}\"", cancellationToken);

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('\"{schemaName}\".\"{sequenceName}\"')";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var value = Convert.ToInt64(result);
            return $"{prefix}-{value:D6}";
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void EnsureSafeIdentifier(string value, string paramName)
    {
        if (!SafeIdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Postgres identifier.", paramName);
        }
    }
}
```

- [ ] **Step 3: Write the provisioning integration test**

Create `apps/api/tests/Api.Tests.Integration/TenantProvisioningServiceTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantProvisioningServiceTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task ProvisionAsync_CreatesTenantRowAndPostgresSchema()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var service = new TenantProvisioningService(dbContext);
        var slug = TenantSlug.Create($"prov-{Guid.NewGuid():N}"[..15]);

        // Act
        var tenant = await service.ProvisionAsync(slug, "Provisioning Test Tenant");

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @schemaName", connection);
        command.Parameters.AddWithValue("schemaName", tenant.SchemaName);
        var schemaCount = (long)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, schemaCount);
    }
}
```

- [ ] **Step 4: Write the sequence generator integration test**

Create `apps/api/tests/Api.Tests.Integration/TenantSequenceIdGeneratorTests.cs`:

```csharp
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantSequenceIdGeneratorTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task NextAsync_CalledRepeatedly_ProducesSequentialFormattedIds()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var generator = new TenantSequenceIdGenerator(dbContext);
        var schemaName = "admin";
        var sequenceName = $"seq_test_{Guid.NewGuid():N}";

        // Act
        var first = await generator.NextAsync(schemaName, sequenceName, "INV");
        var second = await generator.NextAsync(schemaName, sequenceName, "INV");

        // Assert
        Assert.Equal("INV-000001", first);
        Assert.Equal("INV-000002", second);
    }

    [Theory]
    [InlineData("bad schema")]
    [InlineData("bad;schema")]
    [InlineData("BadSchema")]
    public async Task NextAsync_UnsafeSchemaName_ThrowsArgumentException(string schemaName)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var generator = new TenantSequenceIdGenerator(dbContext);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => generator.NextAsync(schemaName, "seq_test", "INV"));
    }
}
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0` (3 from Tasks 1/3 + 1 provisioning + 2 sequence generator).

- [ ] **Step 6: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add TenantProvisioningService and TenantSequenceIdGenerator

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: `TenantDbContext` and per-tenant migration runner

**Files:**
- Create: `apps/api/src/Api.Infrastructure/TenantDbContext.cs`
- Create: `apps/api/src/Api.Infrastructure/SchemaAwareModelCacheKeyFactory.cs`
- Create: `apps/api/src/Api.Infrastructure/TenantMigrationRunner.cs`
- Create: `apps/api/tests/Api.Tests.Integration/TenantMigrationRunnerTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`/`Tenant` (to enumerate provisioned tenants), `TenantProvisioningService` (test setup only).
- Produces: `TenantDbContext` (dynamic schema via constructor parameter, zero `DbSet`s today — a shell for future product entities), `TenantMigrationRunner.MigrateAllTenantsAsync()` and `.MigrateTenantAsync(string schemaName)`.

- [ ] **Step 1: Implement `TenantDbContext`**

Create `apps/api/src/Api.Infrastructure/TenantDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantDbContext : DbContext
{
    public string SchemaName { get; }

    public TenantDbContext(DbContextOptions<TenantDbContext> options, string schemaName) : base(options)
    {
        SchemaName = schemaName;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
```

(No entities yet — see the phase spec's "Kapsam kararı" for why. `Database.MigrateAsync()` still creates a `__EFMigrationsHistory` table in the target schema even with zero migrations defined, which is exactly what Task 5's test verifies.)

- [ ] **Step 2: Implement the schema-aware model cache key factory**

Create `apps/api/src/Api.Infrastructure/SchemaAwareModelCacheKeyFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Api.Infrastructure;

public sealed class SchemaAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TenantDbContext tenantDbContext
            ? (tenantDbContext.SchemaName, designTime)
            : (object)designTime;
}
```

This must be registered per-instance when constructing `TenantDbContext`'s options (each tenant gets its own compiled EF model keyed by schema name) — see Step 4.

- [ ] **Step 3: Implement `TenantMigrationRunner`**

Create `apps/api/src/Api.Infrastructure/TenantMigrationRunner.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Api.Infrastructure;

public sealed class TenantMigrationRunner(AdminDbContext adminDbContext, string connectionString)
{
    public async Task MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var schemaNames = await adminDbContext.Tenants
            .AsNoTracking()
            .Select(t => t.SchemaName)
            .ToListAsync(cancellationToken);

        foreach (var schemaName in schemaNames)
        {
            await MigrateTenantAsync(schemaName, cancellationToken);
        }
    }

    public async Task MigrateTenantAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .ReplaceService<IModelCacheKeyFactory, SchemaAwareModelCacheKeyFactory>()
            .Options;

        await using var tenantContext = new TenantDbContext(options, schemaName);
        await tenantContext.Database.MigrateAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Write the migration runner integration test**

Create `apps/api/tests/Api.Tests.Integration/TenantMigrationRunnerTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantMigrationRunnerTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task MigrateTenantAsync_CreatesMigrationsHistoryTableInTenantSchema()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var slug = TenantSlug.Create($"mig-{Guid.NewGuid():N}"[..15]);
        var tenant = await provisioningService.ProvisionAsync(slug, "Migration Test Tenant");
        var runner = new TenantMigrationRunner(dbContext, fixture.ConnectionString);

        // Act
        await runner.MigrateTenantAsync(tenant.SchemaName);

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schemaName AND table_name = '__EFMigrationsHistory'",
            connection);
        command.Parameters.AddWithValue("schemaName", tenant.SchemaName);
        var tableCount = (long)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, tableCount);
    }

    [Fact]
    public async Task MigrateAllTenantsAsync_MigratesEveryProvisionedTenant()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var firstTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"all-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var secondTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"all-b-{Guid.NewGuid():N}"[..15]), "Tenant B");
        var runner = new TenantMigrationRunner(dbContext, fixture.ConnectionString);

        // Act
        await runner.MigrateAllTenantsAsync();

        // Assert
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        foreach (var schemaName in new[] { firstTenant.SchemaName, secondTenant.SchemaName })
        {
            await using var command = new NpgsqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schemaName AND table_name = '__EFMigrationsHistory'",
                connection);
            command.Parameters.AddWithValue("schemaName", schemaName);
            var tableCount = (long)(await command.ExecuteScalarAsync())!;
            Assert.Equal(1, tableCount);
        }
    }
}
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0` (6 from Tasks 1/3/4 + 2 new).

- [ ] **Step 6: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add TenantDbContext and per-tenant migration runner

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Optimistic concurrency and audit-timestamp proof tests

**Files:**
- Create: `apps/api/tests/Api.Tests.Integration/MembershipConcurrencyTests.cs`
- Create: `apps/api/tests/Api.Tests.Integration/AuditableInterceptorTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`, `AuditableSaveChangesInterceptor`, `Membership`, `Tenant`, `User` from Tasks 2-3. No new production code — this task only adds tests proving behavior that Task 3 already wired but never directly exercised end-to-end.

- [ ] **Step 1: Write the concurrency conflict test**

Create `apps/api/tests/Api.Tests.Integration/MembershipConcurrencyTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class MembershipConcurrencyTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task ConcurrentRoleChange_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        // Arrange
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "member");

        await using (var seedContext = CreateContext())
        {
            seedContext.Memberships.Add(membership);
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstView = await firstContext.Memberships.SingleAsync(m => m.Id == membership.Id);
        var secondView = await secondContext.Memberships.SingleAsync(m => m.Id == membership.Id);

        // Act
        firstView.ChangeRole("admin");
        await firstContext.SaveChangesAsync();

        secondView.ChangeRole("owner");

        // Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Write the audit interceptor test**

Create `apps/api/tests/Api.Tests.Integration/AuditableInterceptorTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AuditableInterceptorTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new AuditableSaveChangesInterceptor())
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task AddUser_SetsCreatedAndUpdatedAtUtcOnInsert()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));

        // Act
        await using var context = CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.InRange(user.CreatedAtUtc, before, after);
        Assert.InRange(user.UpdatedAtUtc, before, after);
    }

    [Fact]
    public async Task UpdateUser_OnlyUpdatesAtUtcChanges_CreatedAtUtcStaysFixed()
    {
        // Arrange
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));

        await using var seedContext = CreateContext();
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();
        var originalCreatedAt = user.CreatedAtUtc;

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Act
        user.SetTimeZone("Europe/Istanbul");
        await seedContext.SaveChangesAsync();

        // Assert
        Assert.Equal(originalCreatedAt, user.CreatedAtUtc);
        Assert.True(user.UpdatedAtUtc > originalCreatedAt);
    }
}
```

- [ ] **Step 3: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`
Expected: `Passed! - Failed: 0, Passed: 11, Skipped: 0` (8 from Tasks 1/3/4/5 + 3 new).

- [ ] **Step 4: Run the full solution once, this is the last task**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

Run: `pnpm --filter api build` and `pnpm --filter api test` from repo root.
Expected: both succeed (matches the phase spec's acceptance criterion 7).

- [ ] **Step 5: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "test(api): prove Membership optimistic concurrency and audit interceptor behavior

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
