using Application.Abstractions;
using Application.Results;
using FluentValidation;

namespace Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new Dictionary<string, List<string>>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);

            foreach (var failure in result.Errors)
            {
                if (!failures.TryGetValue(failure.PropertyName, out var messages))
                {
                    messages = [];
                    failures[failure.PropertyName] = messages;
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        return Error.Validation(
            failures.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
    }
}
