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
