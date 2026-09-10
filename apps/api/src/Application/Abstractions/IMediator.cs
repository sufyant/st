using Application.Results;

namespace Application.Abstractions;

public interface IMediator
{
    Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken);
}
