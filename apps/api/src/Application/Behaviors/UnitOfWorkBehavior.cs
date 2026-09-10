using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse>(
    IServiceProvider provider,
    TenantContext tenantContext) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICommand<TResponse>)
        {
            return await next();
        }

        var result = await next();

        if (!result.IsSuccess)
        {
            return result;
        }

        // Two databases are two units of work; this is not one atomic write. The order is fixed so
        // that a crash between the two saves closes access rather than leaving it half open.
        var controlPlane = provider.GetRequiredService<ControlPlaneDbContext>();

        if (controlPlane.ChangeTracker.HasChanges())
        {
            await controlPlane.SaveChangesAsync(cancellationToken);
        }

        if (!tenantContext.IsResolved)
        {
            return result;
        }

        var tenant = provider.GetRequiredService<TenantDbContext>();

        if (tenant.ChangeTracker.HasChanges())
        {
            await tenant.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
