using Application.Abstractions;
using Application.Results;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse>(
    IMemoryCache cache,
    TenantContext tenantContext) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICachedQuery query)
        {
            return await next();
        }

        // The tenant prefix belongs to the behavior, never to the query. A query author who
        // forgets it serves one tenant's answer to another.
        var key = $"{tenantContext.TenantId.Value:N}:{query.CacheKey}";

        if (cache.TryGetValue(key, out TResponse? cached))
        {
            return cached!;
        }

        var result = await next();

        if (result.IsSuccess)
        {
            cache.Set(key, result.Value, query.Duration);
        }

        return result;
    }
}
