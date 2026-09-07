# apps/api Phase 2a: Domain Building Blocks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent` base classes and `Email`/`Money`/`TenantSlug` value objects to `Api.Domain`, each with real xUnit tests in `Api.Tests.Unit/Domain/`.

**Architecture:** Pure domain layer additions — no EF Core, no database, no Testcontainers. `Api.Domain` stays dependency-free (BCL only).

**Tech Stack:** C# 13 / .NET 10, xUnit (already referenced by `Api.Tests.Unit` from Phase 1).

## Global Constraints

- `Api.Domain.csproj` gains zero `PackageReference` entries in this phase — every type here is BCL-only.
- Value object `Create` factories throw `ArgumentException` on invalid input (not `Result<T>` — see the phase spec's "Result<T> ile ilişkisi" section for why; `Result<T>` lands in a later phase).
- All new domain types live directly under `apps/api/src/Api.Domain/` (no subfolders yet — the project is small; introduce subfolders only when it actually grows unwieldy).
- All new tests live under `apps/api/tests/Api.Tests.Unit/Domain/`.
- Every test file uses AAA comments (`// Arrange`, `// Act`, `// Assert`) — the one exception to this repo's no-comments rule ([ADR 0025](../../adr/0025-test-strategy.md), [ADR 0027](../../adr/0027-code-standard.md)).
- Work from `/Users/sufyan/Documents/Projects/st` (repo root).

---

### Task 1: `Entity<TId>`, `AggregateRoot<TId>`, `DomainEvent`

**Files:**
- Create: `apps/api/src/Api.Domain/Entity.cs`
- Create: `apps/api/src/Api.Domain/DomainEvent.cs`
- Create: `apps/api/src/Api.Domain/AggregateRoot.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/EntityTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/AggregateRootTests.cs`

**Interfaces:**
- Consumes: nothing (first domain types in the project besides the empty skeleton from Phase 1).
- Produces: `Entity<TId>` (identity equality via `Id`), `AggregateRoot<TId>` (adds `DomainEvents`, `Raise(DomainEvent)`, `ClearDomainEvents()`), `DomainEvent` (abstract record with `OccurredOnUtc`). Task 2's `ValueObject`/`Email`/`Money`/`TenantSlug` do not depend on these, but later phases (Phase 2b's real entities) will inherit from both.

- [ ] **Step 1: Write the failing tests for `Entity<TId>`**

Create `apps/api/tests/Api.Tests.Unit/Domain/EntityTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class EntityTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id) : base(id) { }
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) : base(id) { }
    }

    [Fact]
    public void Equals_SameTypeAndId_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        // Act
        var areEqual = first.Equals(second);

        // Assert
        Assert.True(areEqual);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        // Arrange
        var first = new TestEntity(Guid.NewGuid());
        var second = new TestEntity(Guid.NewGuid());

        // Act & Assert
        Assert.False(first.Equals(second));
    }

    [Fact]
    public void Equals_SameIdDifferentType_ReturnsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);
        var other = new OtherEntity(id);

        // Act & Assert
        Assert.False(entity.Equals(other));
    }

    [Fact]
    public void EqualityOperator_BothNull_ReturnsTrue()
    {
        // Arrange
        TestEntity? left = null;
        TestEntity? right = null;

        // Act & Assert
        Assert.True(left == right);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~EntityTests"`
Expected: build failure — `Api.Domain` namespace has no `Entity<T>` type yet (`CS0246`).

- [ ] **Step 3: Implement `Entity<TId>`**

Create `apps/api/src/Api.Domain/Entity.cs`:

```csharp
namespace Api.Domain;

public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    protected Entity()
    {
    }

    protected Entity(TId id)
    {
        Id = id;
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~EntityTests"`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: Write the failing tests for `AggregateRoot<TId>` and `DomainEvent`**

Create `apps/api/tests/Api.Tests.Unit/Domain/AggregateRootTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class AggregateRootTests
{
    private sealed record TestDomainEvent(string Reason) : DomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate(Guid id) : base(id)
        {
        }

        public void DoSomething(string reason) => Raise(new TestDomainEvent(reason));
    }

    [Fact]
    public void Raise_AddsEventToDomainEvents()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        aggregate.DoSomething("first");

        // Assert
        Assert.Single(aggregate.DomainEvents);
        Assert.Equal("first", Assert.IsType<TestDomainEvent>(aggregate.DomainEvents[0]).Reason);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.DoSomething("first");
        aggregate.DoSomething("second");

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvent_OccurredOnUtc_IsSetToUtcNowAtCreation()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var domainEvent = new TestDomainEvent("reason");
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.InRange(domainEvent.OccurredOnUtc, before, after);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~AggregateRootTests"`
Expected: build failure — `AggregateRoot<T>` and `DomainEvent` don't exist yet (`CS0246`).

- [ ] **Step 7: Implement `DomainEvent`**

Create `apps/api/src/Api.Domain/DomainEvent.cs`:

```csharp
namespace Api.Domain;

public abstract record DomainEvent
{
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 8: Implement `AggregateRoot<TId>`**

Create `apps/api/src/Api.Domain/AggregateRoot.cs`:

```csharp
namespace Api.Domain;

public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~AggregateRootTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 10: Run the full test project once**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0` (4 EntityTests + 3 AggregateRootTests + 1 HealthEndpointTests from Phase 1)

- [ ] **Step 11: Commit**

```bash
git add apps/api/src/Api.Domain/Entity.cs apps/api/src/Api.Domain/DomainEvent.cs apps/api/src/Api.Domain/AggregateRoot.cs apps/api/tests/Api.Tests.Unit/Domain/EntityTests.cs apps/api/tests/Api.Tests.Unit/Domain/AggregateRootTests.cs
git commit -m "feat(api): add Entity, AggregateRoot, DomainEvent base classes

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `ValueObject`, `Email`, `Money`, `TenantSlug`

**Files:**
- Create: `apps/api/src/Api.Domain/ValueObject.cs`
- Create: `apps/api/src/Api.Domain/Email.cs`
- Create: `apps/api/src/Api.Domain/Money.cs`
- Create: `apps/api/src/Api.Domain/TenantSlug.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/EmailTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/MoneyTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/TenantSlugTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (value objects are independent of `Entity`/`AggregateRoot`).
- Produces: `ValueObject` (structural equality via `GetEqualityComponents()`), `Email.Create(string) : Email`, `Money.Create(decimal, string) : Money` + `Money.Zero(string) : Money` + `Money.Add(Money) : Money`, `TenantSlug.Create(string) : TenantSlug`. Phase 2b's real entities (`Tenant`, `Membership`, etc.) will use `TenantSlug` and `Email`.

- [ ] **Step 1: Write the failing tests for `Email`**

Create `apps/api/tests/Api.Tests.Unit/Domain/EmailTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@Example.COM")]
    [InlineData("  user@example.com  ")]
    public void Create_ValidInput_NormalizesToLowercaseTrimmed(string input)
    {
        // Act
        var email = Email.Create(input);

        // Assert
        Assert.Equal("user@example.com", email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    public void Create_InvalidInput_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Email.Create(input));
    }

    [Fact]
    public void Equals_SameValueDifferentCase_ReturnsTrue()
    {
        // Arrange
        var first = Email.Create("user@example.com");
        var second = Email.Create("USER@EXAMPLE.COM");

        // Act & Assert
        Assert.Equal(first, second);
    }
}
```

- [ ] **Step 2: Write the failing tests for `Money`**

Create `apps/api/tests/Api.Tests.Unit/Domain/MoneyTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class MoneyTests
{
    [Fact]
    public void Create_ValidInput_NormalizesCurrencyToUppercase()
    {
        // Act
        var money = Money.Create(10.50m, "usd");

        // Assert
        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Create_InvalidCurrency_ThrowsArgumentException(string currency)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Money.Create(1m, currency));
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        // Arrange
        var first = Money.Create(10m, "USD");
        var second = Money.Create(5m, "USD");

        // Act
        var result = first.Add(second);

        // Assert
        Assert.Equal(Money.Create(15m, "USD"), result);
    }

    [Fact]
    public void Add_DifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var usd = Money.Create(10m, "USD");
        var eur = Money.Create(5m, "EUR");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => usd.Add(eur));
    }

    [Fact]
    public void Zero_ReturnsZeroAmountInGivenCurrency()
    {
        // Act
        var zero = Money.Zero("EUR");

        // Assert
        Assert.Equal(0m, zero.Amount);
        Assert.Equal("EUR", zero.Currency);
    }
}
```

- [ ] **Step 3: Write the failing tests for `TenantSlug`**

Create `apps/api/tests/Api.Tests.Unit/Domain/TenantSlugTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("acme-corp-2")]
    public void Create_ValidInput_Succeeds(string input)
    {
        // Act
        var slug = TenantSlug.Create(input);

        // Assert
        Assert.Equal(input, slug.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("acme_corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme--corp")]
    public void Create_InvalidInput_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => TenantSlug.Create(input));
    }

    [Fact]
    public void Create_ExceedsPostgresSchemaNameLimit_ThrowsArgumentException()
    {
        // Arrange
        var tooLong = new string('a', 64);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TenantSlug.Create(tooLong));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~EmailTests|FullyQualifiedName~MoneyTests|FullyQualifiedName~TenantSlugTests"`
Expected: build failure — `Email`, `Money`, `TenantSlug`, `ValueObject` don't exist yet (`CS0246`).

- [ ] **Step 5: Implement `ValueObject`**

Create `apps/api/src/Api.Domain/ValueObject.cs`:

```csharp
namespace Api.Domain;

public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || GetType() != other.GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, HashCode.Combine);

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
```

- [ ] **Step 6: Implement `Email`**

Create `apps/api/src/Api.Domain/Email.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Api.Domain;

public sealed partial class Email : ValueObject
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        var trimmed = value.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(value));
        }

        if (!EmailPattern().IsMatch(trimmed))
        {
            throw new ArgumentException($"'{value}' is not a valid email address.", nameof(value));
        }

        return new Email(trimmed.ToLowerInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 7: Implement `Money`**

Create `apps/api/src/Api.Domain/Money.cs`:

```csharp
namespace Api.Domain;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money Zero(string currency) => Create(0m, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");
        }

        return Create(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount} {Currency}";
}
```

- [ ] **Step 8: Implement `TenantSlug`**

Create `apps/api/src/Api.Domain/TenantSlug.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Api.Domain;

public sealed partial class TenantSlug : ValueObject
{
    private const int PostgresSchemaNameMaxLength = 63;

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public string Value { get; }

    private TenantSlug(string value)
    {
        Value = value;
    }

    public static TenantSlug Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant slug cannot be empty.", nameof(value));
        }

        if (value.Length > PostgresSchemaNameMaxLength)
        {
            throw new ArgumentException(
                $"Tenant slug cannot exceed {PostgresSchemaNameMaxLength} characters (Postgres schema name limit).",
                nameof(value));
        }

        if (!SlugPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid tenant slug (lowercase letters, digits, single hyphens only, no leading/trailing hyphen).",
                nameof(value));
        }

        return new TenantSlug(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~EmailTests|FullyQualifiedName~MoneyTests|FullyQualifiedName~TenantSlugTests"`
Expected: `Passed! - Failed: 0, Passed: 26, Skipped: 0` (EmailTests: 3 + 5 InlineData cases + 1 fact = 9; MoneyTests: 1 fact + 3 InlineData + 1 + 1 + 1 facts = 7; TenantSlugTests: 3 + 6 InlineData cases + 1 fact = 10; 9+7+10=26)

- [ ] **Step 10: Run the full test project once**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
Expected: `Passed! - Failed: 0, Passed: 34, Skipped: 0` (1 HealthEndpointTests from Phase 1 + 4 EntityTests + 3 AggregateRootTests from Task 1 + 26 from this task = 34)

- [ ] **Step 11: Commit**

```bash
git add apps/api/src/Api.Domain/ValueObject.cs apps/api/src/Api.Domain/Email.cs apps/api/src/Api.Domain/Money.cs apps/api/src/Api.Domain/TenantSlug.cs apps/api/tests/Api.Tests.Unit/Domain/EmailTests.cs apps/api/tests/Api.Tests.Unit/Domain/MoneyTests.cs apps/api/tests/Api.Tests.Unit/Domain/TenantSlugTests.cs
git commit -m "feat(api): add ValueObject base and Email/Money/TenantSlug

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
