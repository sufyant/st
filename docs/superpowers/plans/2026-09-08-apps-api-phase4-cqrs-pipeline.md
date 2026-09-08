# apps/api Phase 4: CQRS-lite + Mediator/Pipeline + Outbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hand-rolled mediator + pipeline behaviors (logging, validation, permission, SaveChanges-as-Unit-of-Work), `Result<T>`, and the outbox pattern — demonstrated end-to-end via a real, non-speculative command (`RenameTenantCommand`) dispatched directly through `IMediator.Send(...)`, no HTTP endpoint yet (that's Phase 5).

**Architecture:** `Api.Application` gains its first real content: `Result<T>`, mediator abstractions, `LoggingBehavior`/`ValidationBehavior` (framework-agnostic besides `Microsoft.Extensions.Logging.Abstractions`/`FluentValidation`), and `RenameTenantCommand`+handler+validator depending only on a narrow `ITenantRepository` interface (no EF Core/`AdminDbContext` leak into Application — this preserves the layering Phase 3's final review confirmed was worth keeping). `Api.Infrastructure` gains `TenantRepository` (implements the interface), `PermissionBehavior` (needs `IHttpContextAccessor`), `SaveChangesUnitOfWorkBehavior` (needs `AdminDbContext`, also drains pending domain events into `OutboxMessage` rows in the same `SaveChanges` call), `OutboxMessage` persistence, and `OutboxProcessor` (a `BackgroundService`).

**Tech Stack:** `FluentValidation`, `Microsoft.Extensions.Logging.Abstractions` (Application); existing EF Core/Npgsql (Infrastructure).

## Global Constraints

- `Api.Application` must NOT reference `Microsoft.EntityFrameworkCore` or any EF Core type (including `DbSet<T>`) — this is the layering Phase 3's final review explicitly decided was worth preserving. Persistence access from Application-layer handlers goes through narrow, purpose-built interfaces (like `ITenantRepository`), never a `DbContext`-shaped abstraction.
- `Api.Application` MAY depend on `Microsoft.Extensions.Logging.Abstractions` and `FluentValidation` — these are framework-agnostic, DI-friendly packages, not a persistence or web-hosting leak.
- No new HTTP endpoints in this phase — the mediator pipeline is proven via direct `IMediator.Send(...)` calls in tests, not HTTP requests. API surface is Phase 5.
- `RenameTenantCommand` is a real, legitimate tenant-management capability (not a fabricated demo entity) — it exercises the pipeline using something genuinely useful, consistent with this project's "no speculative business functionality" discipline from Phase 2b/3.
- Pipeline order (binding, matches ADR 0006): `LoggingBehavior` (outermost) → `ValidationBehavior` → `PermissionBehavior` → handler → `SaveChangesUnitOfWorkBehavior`'s post-processing (drains domain events into outbox, then calls `SaveChangesAsync` once) happens AFTER the handler returns, wrapping the handler from the "next" side. `SaveChangesUnitOfWorkBehavior` only saves if the response indicates success (`response is Result { IsSuccess: true }` — `Result<T>` derives from `Result`, so this check covers both response shapes uniformly).
- This mediator uses reflection/`dynamic` dispatch to resolve open-generic `IRequestHandler<,>`/`IPipelineBehavior<,>` registrations from DI (`IServiceProvider.GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(...))`) — a well-established, standard technique for a hand-rolled mediator, but genuinely fiddly to get exactly right. **API-detail allowance applies strongly here**: if the exact reflection/`dynamic` mechanism in this plan doesn't compile or dispatch correctly, adjust to the nearest correct equivalent (e.g. switching between raw `MethodInfo.Invoke` and `dynamic`, or a different DI resolution call) and document the deviation clearly — this is expected, not a sign of doing something wrong.
- `TenantProvisioningService.ProvisionAsync` (Phase 2b) manages its own transaction/`SaveChangesAsync` internally — this phase does NOT touch it or reuse it (that's why the demo command is `RenameTenantCommand`, not tenant provisioning — reusing the provisioning service would conflict with the pipeline's own Unit-of-Work/outbox transaction ownership). Do not modify `TenantProvisioningService` in this phase.
- Work from `/Users/sufyan/Documents/Projects/st` (repo root). Docker must be running for integration tests.

---

### Task 1: `Result<T>` and mediator core abstractions

**Files:**
- Create: `apps/api/src/Api.Application/Result.cs`
- Create: `apps/api/src/Api.Application/IRequest.cs`
- Create: `apps/api/src/Api.Application/IRequestHandler.cs`
- Create: `apps/api/src/Api.Application/IPipelineBehavior.cs`
- Create: `apps/api/src/Api.Application/IMediator.cs`
- Create: `apps/api/src/Api.Application/Mediator.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Application/ResultTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Application/MediatorTests.cs`

**Interfaces:**
- Consumes: nothing new (first real content in `Api.Application`, which currently only references `Api.Domain`).
- Produces: `Result`/`Result<T>` (`IsSuccess`, `IsFailure`, `Error`, `Value`), `IRequest<TResponse>`, `IRequestHandler<TRequest,TResponse>`, `IPipelineBehavior<TRequest,TResponse>`, `RequestHandlerDelegate<TResponse>`, `IMediator.Send<TResponse>(IRequest<TResponse>, CancellationToken) : Task<TResponse>`, `Mediator : IMediator` (constructed with `IServiceProvider`). Task 2's behaviors, Task 3's command/handler, and Task 4's DI wiring all depend on these exact shapes.

- [ ] **Step 1: Write the failing tests for `Result`/`Result<T>`**

Create `apps/api/tests/Api.Tests.Unit/Application/ResultTests.cs`:

```csharp
using Api.Application;
using Xunit;

namespace Api.Tests.Unit.Application;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrue_ErrorIsNone()
    {
        // Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailureTrue_CarriesError()
    {
        // Arrange
        var error = new Error("Some.Code", "Some message");

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericSuccess_ValueAccessible()
    {
        // Act
        var result = Result.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccessingValueThrows()
    {
        // Arrange
        var result = Result.Failure<int>(new Error("Code", "Message"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        // Act
        Result<string> result = "hello";

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }
}
```

- [ ] **Step 2: Write the failing tests for `Mediator`**

Create `apps/api/tests/Api.Tests.Unit/Application/MediatorTests.cs`:

```csharp
using Api.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Unit.Application;

public class MediatorTests
{
    private sealed record Ping(string Message) : IRequest<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) =>
            Task.FromResult($"Pong: {request.Message}");
    }

    private sealed class UppercaseBehavior : IPipelineBehavior<Ping, string>
    {
        public async Task<string> Handle(
            Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            var response = await next();
            return response.ToUpperInvariant();
        }
    }

    [Fact]
    public async Task Send_NoBehaviors_InvokesHandlerDirectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        var provider = services.BuildServiceProvider();
        var mediator = new Mediator(provider);

        // Act
        var response = await mediator.Send(new Ping("hi"));

        // Assert
        Assert.Equal("Pong: hi", response);
    }

    [Fact]
    public async Task Send_WithBehavior_BehaviorWrapsHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        services.AddScoped<IPipelineBehavior<Ping, string>, UppercaseBehavior>();
        var provider = services.BuildServiceProvider();
        var mediator = new Mediator(provider);

        // Act
        var response = await mediator.Send(new Ping("hi"));

        // Assert
        Assert.Equal("PONG: HI", response);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~ResultTests|FullyQualifiedName~MediatorTests"`
Expected: build failure — none of `Result`, `IRequest`, `IRequestHandler`, `IPipelineBehavior`, `IMediator`, `Mediator` exist yet.

- [ ] **Step 4: Implement `Result`/`Result<T>` and `Error`**

Create `apps/api/src/Api.Application/Result.cs`:

```csharp
namespace Api.Application;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public class Result
{
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot have an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must have an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    public static Result<T> Failure<T>(Error error) => new(default!, false, error);
}

public sealed class Result<T> : Result
{
    private readonly T _value;

    internal Result(T value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<T>(T value) => Success(value);
}
```

- [ ] **Step 5: Run the `Result` tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~ResultTests"`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 6: Implement the mediator abstractions**

Create `apps/api/src/Api.Application/IRequest.cs`:

```csharp
namespace Api.Application;

public interface IRequest<TResponse>;
```

Create `apps/api/src/Api.Application/IRequestHandler.cs`:

```csharp
namespace Api.Application;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

Create `apps/api/src/Api.Application/IPipelineBehavior.cs`:

```csharp
namespace Api.Application;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
```

Create `apps/api/src/Api.Application/IMediator.cs`:

```csharp
namespace Api.Application;

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 7: Implement `Mediator`**

Create `apps/api/src/Api.Application/Mediator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Api.Application;

public sealed class Mediator(IServiceProvider serviceProvider) : IMediator
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        dynamic handler = serviceProvider.GetRequiredService(handlerType);

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = serviceProvider.GetServices(behaviorType).Cast<dynamic>().Reverse().ToList();

        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((dynamic)request, cancellationToken);

        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            var currentBehavior = behavior;
            pipeline = () => currentBehavior.Handle((dynamic)request, next, cancellationToken);
        }

        return pipeline();
    }
}
```

If `dynamic` dispatch through the `handler`/`behavior` variables doesn't resolve `Handle` correctly at runtime (e.g. a `RuntimeBinderException`), fall back to explicit reflection (`handlerType.GetMethod("Handle")!.Invoke(handler, [request, cancellationToken])` cast to `Task<TResponse>`, and similarly for each behavior's `Handle(request, next, cancellationToken)`) — document which approach you ended up using and why.

- [ ] **Step 8: Run the mediator tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~MediatorTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 9: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall (100 pre-existing + 7 new = 107; recount from actual output).

- [ ] **Step 10: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add Result<T> and hand-rolled mediator core abstractions

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Logging, validation, and permission-attribute infrastructure

**Files:**
- Modify: `apps/api/src/Api.Application/Api.Application.csproj` (add `FluentValidation`)
- Create: `apps/api/src/Api.Application/LoggingBehavior.cs`
- Create: `apps/api/src/Api.Application/ResultReflectionHelper.cs`
- Create: `apps/api/src/Api.Application/ValidationBehavior.cs`
- Create: `apps/api/src/Api.Application/RequiresPermissionAttribute.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Application/ValidationBehaviorTests.cs`

**Interfaces:**
- Consumes: `Result`/`Result<T>`, `IPipelineBehavior<,>`, `IRequest<>` from Task 1.
- Produces: `LoggingBehavior<TRequest,TResponse>`, `ValidationBehavior<TRequest,TResponse>` (short-circuits to a `Result`/`Result<T>` failure on validation errors, via `ResultReflectionHelper.CreateFailure<TResponse>(Error)`), `RequiresPermissionAttribute(string permission)`. Task 3's `RenameTenantCommand` uses `[RequiresPermission]`; Task 4's `PermissionBehavior` (Infrastructure) reuses `ResultReflectionHelper`.

- [ ] **Step 1: Add the FluentValidation package**

```bash
cd apps/api
dotnet add src/Api.Application/Api.Application.csproj package FluentValidation
```

- [ ] **Step 2: Implement `ResultReflectionHelper`**

Create `apps/api/src/Api.Application/ResultReflectionHelper.cs`:

```csharp
namespace Api.Application;

/// Pipeline behaviors that short-circuit (validation, permission) need to
/// construct a failure of whatever TResponse the current request declares
/// (always Result or Result{T} by this codebase's convention) without
/// knowing T at compile time -- this reflects it.
internal static class ResultReflectionHelper
{
    public static TResponse CreateFailure<TResponse>(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethods()
                .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(valueType);
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"{typeof(TResponse)} is not a supported response type — expected Result or Result<T>.");
    }
}
```

- [ ] **Step 3: Implement `LoggingBehavior`**

Create `apps/api/src/Api.Application/LoggingBehavior.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Api.Application;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
        return response;
    }
}
```

- [ ] **Step 4: Implement `ValidationBehavior`**

Create `apps/api/src/Api.Application/ValidationBehavior.cs`:

```csharp
using FluentValidation;

namespace Api.Application;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        var error = new Error("Validation.Failed", string.Join("; ", failures.Select(f => f.ErrorMessage)));
        return ResultReflectionHelper.CreateFailure<TResponse>(error);
    }
}
```

- [ ] **Step 5: Implement `RequiresPermissionAttribute`**

Create `apps/api/src/Api.Application/RequiresPermissionAttribute.cs`:

```csharp
namespace Api.Application;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
```

- [ ] **Step 6: Write the failing tests for `ValidationBehavior`**

Create `apps/api/tests/Api.Tests.Unit/Application/ValidationBehaviorTests.cs`:

```csharp
using Api.Application;
using FluentValidation;
using Xunit;

namespace Api.Tests.Unit.Application;

public class ValidationBehaviorTests
{
    private sealed record CreateThing(string Name) : IRequest<Result<string>>;

    private sealed class CreateThingValidator : AbstractValidator<CreateThing>
    {
        public CreateThingValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    [Fact]
    public async Task Handle_InvalidRequest_ShortCircuitsWithFailureAndNeverCallsNext()
    {
        // Arrange
        var behavior = new ValidationBehavior<CreateThing, Result<string>>([new CreateThingValidator()]);
        var nextCalled = false;
        RequestHandlerDelegate<Result<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success("unused"));
        };

        // Act
        var result = await behavior.Handle(new CreateThing(""), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        // Arrange
        var behavior = new ValidationBehavior<CreateThing, Result<string>>([new CreateThingValidator()]);
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Result.Success("ok"));

        // Act
        var result = await behavior.Handle(new CreateThing("valid"), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task Handle_NonGenericResultResponse_ShortCircuitsCorrectly()
    {
        // Arrange
        var behavior = new ValidationBehavior<PlainCommand, Result>([new PlainCommandValidator()]);
        RequestHandlerDelegate<Result> next = () => Task.FromResult(Result.Success());

        // Act
        var result = await behavior.Handle(new PlainCommand(""), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
    }

    private sealed record PlainCommand(string Name) : IRequest<Result>;

    private sealed class PlainCommandValidator : AbstractValidator<PlainCommand>
    {
        public PlainCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~ValidationBehaviorTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 8: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 9: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add LoggingBehavior, ValidationBehavior, RequiresPermissionAttribute

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `Tenant.Rename`, `ITenantRepository`, and `RenameTenantCommand`

**Files:**
- Modify: `apps/api/src/Api.Domain/Tenant.cs` (add `Rename(string)` method, raise a domain event)
- Create: `apps/api/src/Api.Domain/TenantRenamedDomainEvent.cs`
- Create: `apps/api/src/Api.Application/Tenants/ITenantRepository.cs`
- Create: `apps/api/src/Api.Application/Tenants/RenameTenantCommand.cs`
- Create: `apps/api/src/Api.Application/Tenants/RenameTenantCommandHandler.cs`
- Create: `apps/api/src/Api.Application/Tenants/RenameTenantCommandValidator.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Domain/TenantRenameTests.cs`
- Create: `apps/api/tests/Api.Tests.Unit/Application/RenameTenantCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `Tenant`, `AggregateRoot<TId>.Raise`, `DomainEvent` from Phase 2a/2b; `IRequest<>`, `IRequestHandler<,>`, `Result`/`Result<T>`, `RequiresPermissionAttribute` from Tasks 1-2.
- Produces: `Tenant.Rename(string newName)`, `TenantRenamedDomainEvent(Guid TenantId, string NewName)`, `ITenantRepository.FindByIdAsync(Guid, CancellationToken) : Task<Tenant?>`, `RenameTenantCommand(Guid TenantId, string NewName) : IRequest<Result>` (marked `[RequiresPermission("tenant.rename")]`). Task 4's `TenantRepository` (Infrastructure) implements `ITenantRepository`; Task 4's DI wiring registers the handler/validator.

- [ ] **Step 1: Write the failing test for `Tenant.Rename`**

Create `apps/api/tests/Api.Tests.Unit/Domain/TenantRenameTests.cs`:

```csharp
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantRenameTests
{
    [Fact]
    public void Rename_ValidInput_UpdatesNameAndRaisesDomainEvent()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");

        // Act
        tenant.Rename("New Name");

        // Assert
        Assert.Equal("New Name", tenant.Name);
        var domainEvent = Assert.Single(tenant.DomainEvents);
        var renamedEvent = Assert.IsType<TenantRenamedDomainEvent>(domainEvent);
        Assert.Equal(tenant.Id, renamedEvent.TenantId);
        Assert.Equal("New Name", renamedEvent.NewName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_EmptyName_ThrowsArgumentException(string newName)
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => tenant.Rename(newName));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~TenantRenameTests"`
Expected: build failure — `Tenant.Rename` and `TenantRenamedDomainEvent` don't exist yet.

- [ ] **Step 3: Implement `TenantRenamedDomainEvent` and `Tenant.Rename`**

Create `apps/api/src/Api.Domain/TenantRenamedDomainEvent.cs`:

```csharp
namespace Api.Domain;

public sealed record TenantRenamedDomainEvent(Guid TenantId, string NewName) : DomainEvent;
```

In `apps/api/src/Api.Domain/Tenant.cs`, add this method to the `Tenant` class (read the current file first, add alongside the existing `Create` factory — don't change anything else):

```csharp
public void Rename(string newName)
{
    if (string.IsNullOrWhiteSpace(newName))
    {
        throw new ArgumentException("Tenant name cannot be empty.", nameof(newName));
    }

    Name = newName;
    Raise(new TenantRenamedDomainEvent(Id, newName));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~TenantRenameTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Implement `ITenantRepository`**

Create `apps/api/src/Api.Application/Tenants/ITenantRepository.cs`:

```csharp
using Api.Domain;

namespace Api.Application.Tenants;

public interface ITenantRepository
{
    Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Write the failing test for `RenameTenantCommandHandler`**

Create `apps/api/tests/Api.Tests.Unit/Application/RenameTenantCommandHandlerTests.cs`:

```csharp
using Api.Application.Tenants;
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Application;

public class RenameTenantCommandHandlerTests
{
    private sealed class FakeTenantRepository(Tenant? tenant) : ITenantRepository
    {
        public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(tenant is not null && tenant.Id == tenantId ? tenant : null);
    }

    [Fact]
    public async Task Handle_ExistingTenant_RenamesAndReturnsSuccess()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");
        var handler = new RenameTenantCommandHandler(new FakeTenantRepository(tenant));

        // Act
        var result = await handler.Handle(new RenameTenantCommand(tenant.Id, "New Name"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", tenant.Name);
    }

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsFailure()
    {
        // Arrange
        var handler = new RenameTenantCommandHandler(new FakeTenantRepository(null));

        // Act
        var result = await handler.Handle(new RenameTenantCommand(Guid.NewGuid(), "New Name"), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }
}
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~RenameTenantCommandHandlerTests"`
Expected: build failure — `RenameTenantCommand`/`RenameTenantCommandHandler` don't exist yet.

- [ ] **Step 8: Implement `RenameTenantCommand`, handler, and validator**

Create `apps/api/src/Api.Application/Tenants/RenameTenantCommand.cs`:

```csharp
namespace Api.Application.Tenants;

[RequiresPermission("tenant.rename")]
public sealed record RenameTenantCommand(Guid TenantId, string NewName) : IRequest<Result>;
```

Create `apps/api/src/Api.Application/Tenants/RenameTenantCommandHandler.cs`:

```csharp
namespace Api.Application.Tenants;

public sealed class RenameTenantCommandHandler(ITenantRepository tenantRepository)
    : IRequestHandler<RenameTenantCommand, Result>
{
    public async Task<Result> Handle(RenameTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure(new Error("Tenant.NotFound", $"Tenant '{request.TenantId}' was not found."));
        }

        tenant.Rename(request.NewName);
        return Result.Success();
    }
}
```

Create `apps/api/src/Api.Application/Tenants/RenameTenantCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Api.Application.Tenants;

public sealed class RenameTenantCommandValidator : AbstractValidator<RenameTenantCommand>
{
    public RenameTenantCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.NewName).NotEmpty();
    }
}
```

Add `using Api.Domain;` to `RenameTenantCommandHandler.cs` if `Tenant`/domain types don't resolve automatically.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj --filter "FullyQualifiedName~RenameTenantCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 10: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 11: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add Tenant.Rename and RenameTenantCommand (first CQRS-lite command)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Infrastructure wiring — repository, permission behavior, Unit of Work, DI

**Files:**
- Create: `apps/api/src/Api.Infrastructure/TenantRepository.cs`
- Create: `apps/api/src/Api.Infrastructure/PermissionBehavior.cs`
- Create: `apps/api/src/Api.Infrastructure/SaveChangesUnitOfWorkBehavior.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (register mediator, open-generic behaviors, handler, validator, repository, `IHttpContextAccessor`)
- Create: `apps/api/tests/Api.Tests.Integration/RenameTenantCommandEndToEndTests.cs`

**Interfaces:**
- Consumes: `Mediator`/`IMediator`, `IPipelineBehavior<,>` from Task 1; `RenameTenantCommand`/handler/validator, `ITenantRepository` from Task 3; `AdminDbContext` from Phase 2b; `PermissionAuthorizationHandler`'s claims convention (`"permission"` claim type) from Phase 3.
- Produces: a fully wired, real-Postgres-backed `IMediator.Send(new RenameTenantCommand(...))` call path. Task 5's outbox work extends `SaveChangesUnitOfWorkBehavior`.

- [ ] **Step 1: Implement `TenantRepository`**

Create `apps/api/src/Api.Infrastructure/TenantRepository.cs`:

```csharp
using Api.Application.Tenants;
using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantRepository(AdminDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
}
```

- [ ] **Step 2: Implement `PermissionBehavior`**

Create `apps/api/src/Api.Infrastructure/PermissionBehavior.cs`:

```csharp
using System.Reflection;
using Api.Application;
using Microsoft.AspNetCore.Http;

namespace Api.Infrastructure;

public sealed class PermissionBehavior<TRequest, TResponse>(IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attribute = typeof(TRequest).GetCustomAttribute<RequiresPermissionAttribute>();

        if (attribute is null)
        {
            return await next();
        }

        var user = httpContextAccessor.HttpContext?.User;
        var hasPermission = user?.HasClaim("permission", attribute.Permission) == true;

        if (!hasPermission)
        {
            var error = new Error(
                "Permission.Denied", $"Missing required permission '{attribute.Permission}'.");
            return ResultReflectionHelperAccessor.CreateFailure<TResponse>(error);
        }

        return await next();
    }
}
```

`ResultReflectionHelper` in `Api.Application` is `internal` — `Api.Infrastructure` cannot call it directly. Add `[assembly: InternalsVisibleTo("Api.Infrastructure")]` to `apps/api/src/Api.Application`'s `AssemblyInfo` (create `apps/api/src/Api.Application/AssemblyInfo.cs` if one doesn't exist, containing just that one line with `using System.Runtime.CompilerServices;`), then reference `ResultReflectionHelper.CreateFailure<TResponse>(error)` directly (remove the `ResultReflectionHelperAccessor` placeholder name above — that was illustrative, not real code; call `Api.Application.ResultReflectionHelper.CreateFailure<TResponse>(error)` once `InternalsVisibleTo` is in place).

- [ ] **Step 3: Implement `SaveChangesUnitOfWorkBehavior`**

Create `apps/api/src/Api.Infrastructure/SaveChangesUnitOfWorkBehavior.cs`:

```csharp
using Api.Application;

namespace Api.Infrastructure;

public sealed class SaveChangesUnitOfWorkBehavior<TRequest, TResponse>(AdminDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (response is Result { IsSuccess: false })
        {
            return response;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }
}
```

(This task does not yet drain domain events into the outbox — that's Task 5, which will extend this same file.)

- [ ] **Step 4: Wire everything into `Program.cs`**

Read the current `apps/api/src/Api.Host/Program.cs` first. Add these registrations after the existing `AddDbContext<AdminDbContext>(...)` call (order among these additions doesn't matter, but all must be present):

```csharp
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<Api.Application.IMediator, Api.Application.Mediator>();

builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Application.LoggingBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Application.ValidationBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Infrastructure.PermissionBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Infrastructure.SaveChangesUnitOfWorkBehavior<,>));

builder.Services.AddScoped<Api.Application.Tenants.ITenantRepository, Api.Infrastructure.TenantRepository>();
builder.Services.AddScoped<
    Api.Application.IRequestHandler<Api.Application.Tenants.RenameTenantCommand, Api.Application.Result>,
    Api.Application.Tenants.RenameTenantCommandHandler>();
builder.Services.AddScoped<
    FluentValidation.IValidator<Api.Application.Tenants.RenameTenantCommand>,
    Api.Application.Tenants.RenameTenantCommandValidator>();
```

**Registration order for the open-generic `IPipelineBehavior<,>` matters** — `Mediator.Send`'s `.Reverse()` call means behaviors registered FIRST end up OUTERMOST in the actual execution order. Registering `LoggingBehavior` first, then `ValidationBehavior`, then `PermissionBehavior`, then `SaveChangesUnitOfWorkBehavior` (as shown above) should produce the binding order: Logging (outermost) → Validation → Permission → handler → SaveChanges-as-UnitOfWork (wraps the handler from the inside, since it does its work AFTER `next()` returns). If the actual observed order doesn't match this once you write Step 5's test, trace through `Mediator.Send`'s reversal logic and adjust the registration order (not the `Mediator` logic) to produce the correct binding sequence — document what you found.

- [ ] **Step 5: Write the end-to-end integration test**

Create `apps/api/tests/Api.Tests.Integration/RenameTenantCommandEndToEndTests.cs`:

```csharp
using System.Security.Claims;
using Api.Application;
using Api.Application.Tenants;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class RenameTenantCommandEndToEndTests(PostgresContainerFixture fixture)
{
    private (IMediator Mediator, AdminDbContext DbContext) BuildMediator(ClaimsPrincipal? user)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = user is null ? null : new DefaultHttpContext { User = user },
        };
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);

        services.AddScoped<IMediator, Mediator>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SaveChangesUnitOfWorkBehavior<,>));

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IRequestHandler<RenameTenantCommand, Result>, RenameTenantCommandHandler>();
        services.AddScoped<FluentValidation.IValidator<RenameTenantCommand>, RenameTenantCommandValidator>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(), scope.ServiceProvider.GetRequiredService<AdminDbContext>());
    }

    private static ClaimsPrincipal AuthenticatedUserWithPermission(string permission)
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", $"clerk_{Guid.NewGuid():N}"), new Claim("permission", permission)],
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Send_ValidCommandWithPermission_RenamesTenantInDatabase()
    {
        // Arrange
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("tenant.rename"));
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, "Renamed"));

        // Assert
        Assert.True(result.IsSuccess);
        await using var verifyContext = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options is var options
            ? new AdminDbContext(options)
            : throw new InvalidOperationException();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Renamed", reloaded.Name);
    }

    [Fact]
    public async Task Send_InvalidCommand_ReturnsFailureAndDoesNotPersist()
    {
        // Arrange
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("tenant.rename"));
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, ""));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Validation.Failed", result.Error.Code);
    }

    [Fact]
    public async Task Send_NoPermission_ReturnsFailureAndDoesNotPersist()
    {
        // Arrange
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("some.other.permission"));
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, "Should Not Apply"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Permission.Denied", result.Error.Code);

        await using var verifyContext = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options is var options
            ? new AdminDbContext(options)
            : throw new InvalidOperationException();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Original Name", reloaded.Name);
    }
}
```

If the `is var options ? new AdminDbContext(options) : throw ...` pattern-match-as-cast-avoidance idiom looks awkward or doesn't compile cleanly, simplify to the same two-step form used elsewhere in this codebase: build `var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;` on its own line, then `await using var verifyContext = new AdminDbContext(options);` — use whichever compiles; the awkward inline form was only to keep the step count down, correctness matters more than the exact shape here.

- [ ] **Step 6: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~RenameTenantCommandEndToEndTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 7: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 8: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): wire mediator pipeline into DI, prove RenameTenantCommand end-to-end

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Outbox pattern

**Files:**
- Create: `apps/api/src/Api.Domain/OutboxMessage.cs`
- Create: `apps/api/src/Api.Infrastructure/Configurations/Admin/OutboxMessageConfiguration.cs`
- Modify: `apps/api/src/Api.Infrastructure/AdminDbContext.cs` (add `DbSet<OutboxMessage>`)
- Modify: `apps/api/src/Api.Infrastructure/SaveChangesUnitOfWorkBehavior.cs` (drain domain events into outbox before saving)
- Create: `apps/api/src/Api.Infrastructure/OutboxProcessor.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (register `OutboxProcessor` as a hosted service)
- Create migration for the `OutboxMessages` table.
- Create: `apps/api/tests/Api.Tests.Integration/OutboxTests.cs`

**Interfaces:**
- Consumes: `IHasDomainEvents`, `DomainEvent` from Phase 2a; `AdminDbContext`, `SaveChangesUnitOfWorkBehavior` from Task 4; `TenantRenamedDomainEvent` from Task 3.
- Produces: `OutboxMessage` (Id, Type, Content, OccurredOnUtc, ProcessedAtUtc), `OutboxProcessor` (a `BackgroundService` polling every 5-10 seconds).

- [ ] **Step 1: Implement `OutboxMessage`**

Create `apps/api/src/Api.Domain/OutboxMessage.cs`:

```csharp
namespace Api.Domain;

public sealed class OutboxMessage : Entity<Guid>
{
    public string Type { get; private set; } = null!;

    public string Content { get; private set; } = null!;

    public DateTimeOffset OccurredOnUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    private OutboxMessage()
    {
    }

    private OutboxMessage(Guid id, string type, string content, DateTimeOffset occurredOnUtc) : base(id)
    {
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
    }

    public static OutboxMessage FromDomainEvent(DomainEvent domainEvent, string type, string content) =>
        new(domainEvent.Id, type, content, domainEvent.OccurredOnUtc);

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
    }
}
```

- [ ] **Step 2: Add the EF configuration and `DbSet`**

Create `apps/api/src/Api.Infrastructure/Configurations/Admin/OutboxMessageConfiguration.cs`:

```csharp
using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Configurations.Admin;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Content).IsRequired();

        builder.HasIndex(m => m.ProcessedAtUtc);
    }
}
```

Read `apps/api/src/Api.Infrastructure/AdminDbContext.cs` and add `public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();` alongside the existing `DbSet` properties.

- [ ] **Step 3: Extend `SaveChangesUnitOfWorkBehavior` to drain domain events into the outbox**

Read the current `apps/api/src/Api.Infrastructure/SaveChangesUnitOfWorkBehavior.cs` (from Task 4) and replace its `Handle` method body with:

```csharp
public async Task<TResponse> Handle(
    TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
{
    var response = await next();

    if (response is Result { IsSuccess: false })
    {
        return response;
    }

    foreach (var entry in dbContext.ChangeTracker.Entries<IHasDomainEvents>())
    {
        foreach (var domainEvent in entry.Entity.DomainEvents)
        {
            var content = System.Text.Json.JsonSerializer.Serialize(
                domainEvent, domainEvent.GetType(), SerializerOptions);
            dbContext.OutboxMessages.Add(
                OutboxMessage.FromDomainEvent(domainEvent, domainEvent.GetType().Name, content));
        }

        entry.Entity.ClearDomainEvents();
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return response;
}

private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new();
```

(Add `using Api.Domain;` if `IHasDomainEvents` doesn't resolve.) `System.Text.Json.JsonSerializer.Serialize(object, Type, options)` serializes using the domain event's ACTUAL runtime type (so derived-record-specific properties like `TenantRenamedDomainEvent.NewName` are included, not just the base `DomainEvent.OccurredOnUtc`/`Id`) — this matters, don't simplify to the generic `Serialize<DomainEvent>` overload, which would only serialize the base type's members.

- [ ] **Step 4: Generate the migration**

```bash
cd apps/api
dotnet ef migrations add AddOutboxMessages --project src/Api.Infrastructure --context AdminDbContext --output-dir Migrations
```

(If `dotnet ef` needs the design-time factory or `--startup-project`, follow the same pattern established in Phase 2b's `AdminDbContextFactory`.)

- [ ] **Step 5: Implement `OutboxProcessor`**

Create `apps/api/src/Api.Infrastructure/OutboxProcessor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure;

public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox processing failed for this cycle.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AdminDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await dbContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.MarkProcessed(DateTimeOffset.UtcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
```

If `FromSqlRaw` with a raw `SELECT *` doesn't map cleanly onto the `OutboxMessage` entity (EF Core's `FromSqlRaw` on a `DbSet<T>` requires the query to return all mapped columns of `T`, in a compatible shape), adjust the SQL/mapping approach — e.g. explicitly list columns matching the entity's mapped properties in the exact order, or use a raw `DbConnection`/`DbCommand` to fetch just the IDs via `FOR UPDATE SKIP LOCKED` and then load those specific entities through the normal `DbSet` — document whichever approach you land on.

- [ ] **Step 6: Register `OutboxProcessor` as a hosted service**

In `apps/api/src/Api.Host/Program.cs`, add:

```csharp
builder.Services.AddHostedService<Api.Infrastructure.OutboxProcessor>();
```

- [ ] **Step 7: Write the outbox integration tests**

Create `apps/api/tests/Api.Tests.Integration/OutboxTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class OutboxTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    [Fact]
    public async Task RenamingTenantThroughMediator_WritesOutboxMessageInSameTransaction()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var tenant = Tenant.Create(TenantSlug.Create($"outbox-{Guid.NewGuid():N}"[..15]), "Original");
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        tenant.ClearDomainEvents();

        // Act — exercise the same rename+outbox mechanism the mediator pipeline uses,
        // directly, without needing the full DI-wired mediator stack from Task 4.
        tenant.Rename("Renamed via outbox test");
        foreach (var domainEvent in tenant.DomainEvents)
        {
            var content = System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            dbContext.OutboxMessages.Add(OutboxMessage.FromDomainEvent(domainEvent, domainEvent.GetType().Name, content));
        }
        tenant.ClearDomainEvents();
        await dbContext.SaveChangesAsync();

        // Assert
        var message = await dbContext.OutboxMessages
            .SingleAsync(m => m.Type == nameof(TenantRenamedDomainEvent) && m.ProcessedAtUtc == null);
        Assert.Contains("Renamed via outbox test", message.Content);
    }

    [Fact]
    public async Task TwoConcurrentProcessorRuns_DoNotDoubleProcessTheSameMessage()
    {
        // Arrange
        await using var seedContext = CreateDbContext();
        var message = OutboxMessage.FromDomainEvent(
            new TenantRenamedDomainEvent(Guid.NewGuid(), "Concurrent Test"),
            nameof(TenantRenamedDomainEvent),
            "{}");
        seedContext.OutboxMessages.Add(message);
        await seedContext.SaveChangesAsync();

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();

        // Act — simulate two concurrent processor cycles racing for the same row.
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        var firstBatch = await firstContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        // Second transaction, started while the first still holds the row lock, must
        // skip it entirely (not block, not error) thanks to SKIP LOCKED.
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        var secondBatch = await secondContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        foreach (var claimed in firstBatch)
        {
            claimed.MarkProcessed(DateTimeOffset.UtcNow);
        }
        await firstContext.SaveChangesAsync();
        await firstTransaction.CommitAsync();

        foreach (var claimed in secondBatch)
        {
            claimed.MarkProcessed(DateTimeOffset.UtcNow);
        }
        await secondContext.SaveChangesAsync();
        await secondTransaction.CommitAsync();

        // Assert
        Assert.Contains(firstBatch, m => m.Id == message.Id);
        Assert.DoesNotContain(secondBatch, m => m.Id == message.Id);
    }
}
```

If the raw `FromSqlRaw`/`FOR UPDATE SKIP LOCKED` query shape used in `OutboxProcessor.cs` (Step 5) needed adjustment there, mirror the same working shape here — these two files' queries must match.

- [ ] **Step 8: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~OutboxTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 9: Run the full solution and the pnpm gates**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

Run (from repo root): `pnpm --filter api build` and `pnpm --filter api test`
Expected: both succeed.

- [ ] **Step 10: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add outbox pattern with BackgroundService processor (SELECT FOR UPDATE SKIP LOCKED)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
