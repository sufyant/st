using Api.Application;
using Api.Domain;

namespace Api.Infrastructure;

public sealed class SaveChangesUnitOfWorkBehavior<TRequest, TResponse>(AdminDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new();

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
}
