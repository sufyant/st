using Application.Abstractions;
using Application.Behaviors;
using Application.Results;
using Domain.ControlPlane.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace UnitTests.Behaviors;

public sealed record CountQuery : IQuery<int>, ICachedQuery
{
    public string CacheKey => "members.count";

    public TimeSpan Duration => TimeSpan.FromMinutes(1);
}

public sealed record UncachedQuery : IQuery<int>;

public sealed class CachingBehaviorTests
{
    [Fact]
    public async Task HandleAsync_SecondCall_NeverReachesTheHandler()
    {
        // Arrange
        var cache = NewCache();
        var calls = 0;
        var behavior = NewBehavior<CountQuery, int>(cache, Guid.CreateVersion7());

        // Act
        await behavior.HandleAsync(new CountQuery(), Counting(), TestContext.Current.CancellationToken);
        var result = await behavior.HandleAsync(
            new CountQuery(),
            Counting(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, calls);
        Assert.Equal(1, result.Value);

        RequestHandlerDelegate<int> Counting() => () =>
        {
            calls++;

            return Task.FromResult(Result<int>.Success(calls));
        };
    }

    [Fact]
    public async Task HandleAsync_ForTwoTenants_NeverSharesOneCacheKey()
    {
        // Arrange
        var cache = NewCache();
        var first = NewBehavior<CountQuery, int>(cache, Guid.CreateVersion7());
        var second = NewBehavior<CountQuery, int>(cache, Guid.CreateVersion7());

        // Act
        var firstResult = await first.HandleAsync(
            new CountQuery(),
            () => Task.FromResult(Result<int>.Success(1)),
            TestContext.Current.CancellationToken);
        var secondResult = await second.HandleAsync(
            new CountQuery(),
            () => Task.FromResult(Result<int>.Success(2)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, firstResult.Value);
        Assert.Equal(2, secondResult.Value);
    }

    [Fact]
    public async Task HandleAsync_WithAFailure_CachesNothing()
    {
        // Arrange
        var cache = NewCache();
        var calls = 0;
        var behavior = NewBehavior<CountQuery, int>(cache, Guid.CreateVersion7());

        // Act
        await behavior.HandleAsync(new CountQuery(), Failing(), TestContext.Current.CancellationToken);
        await behavior.HandleAsync(new CountQuery(), Failing(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, calls);

        RequestHandlerDelegate<int> Failing() => () =>
        {
            calls++;

            return Task.FromResult(Result<int>.Failure(Error.NotFound("missing", "Missing.")));
        };
    }

    [Fact]
    public async Task HandleAsync_ForAQueryThatIsNotCached_AlwaysReachesTheHandler()
    {
        // Arrange
        var cache = NewCache();
        var calls = 0;
        var behavior = NewBehavior<UncachedQuery, int>(cache, Guid.CreateVersion7());

        // Act
        await behavior.HandleAsync(new UncachedQuery(), Counting(), TestContext.Current.CancellationToken);
        await behavior.HandleAsync(new UncachedQuery(), Counting(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, calls);

        RequestHandlerDelegate<int> Counting() => () =>
        {
            calls++;

            return Task.FromResult(Result<int>.Success(calls));
        };
    }

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static CachingBehavior<TRequest, TResponse> NewBehavior<TRequest, TResponse>(
        IMemoryCache cache,
        Guid tenantId)
        where TRequest : IRequest<TResponse>
    {
        var tenantContext = new TenantContext();
        tenantContext.Set(TenantId.From(tenantId), "acme", $"tenant_{tenantId:N}");

        return new CachingBehavior<TRequest, TResponse>(cache, tenantContext);
    }
}
