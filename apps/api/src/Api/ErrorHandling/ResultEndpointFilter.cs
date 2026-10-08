using SharedKernel;

namespace Api.ErrorHandling;

// Endpoints return the Result of their command; this maps it to HTTP once for every endpoint.
internal sealed class ResultEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var returned = await next(context);

        return returned is Result result ? ToHttpResult(result) : returned;
    }

    private static IResult ToHttpResult(Result result)
    {
        if (!result.IsSuccess)
        {
            return TypedResults.Problem(
                statusCode: StatusCodeFor(result.Error.Type),
                detail: result.Error.Description,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        }

        // Result<T> is generic, and the filter runs for endpoints of every T.
        var type = result.GetType();
        return type.IsGenericType
            ? TypedResults.Ok(type.GetProperty(nameof(Result<object>.Value))!.GetValue(result))
            : TypedResults.NoContent();
    }

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Every error type maps to a status code."),
    };
}
