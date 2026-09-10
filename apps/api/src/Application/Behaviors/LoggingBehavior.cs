using System.Diagnostics;
using Application.Abstractions;
using Application.Results;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await next();
            stopwatch.Stop();

            // The request itself is never logged: it can carry an invitation token or an email.
            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "{RequestName} succeeded in {ElapsedMilliseconds}ms.",
                    name,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "{RequestName} failed as {ErrorKind} ({ErrorCode}) in {ElapsedMilliseconds}ms.",
                    name,
                    result.Error.Kind,
                    result.Error.Code,
                    stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "{RequestName} threw after {ElapsedMilliseconds}ms.",
                name,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
