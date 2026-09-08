using Api.Application;

namespace Api.Host;

public static class ResultHttpMapper
{
    public static IResult ToHttpResult(this Result result, ILogger logger) =>
        result.IsSuccess ? Results.NoContent() : MapError(result.Error, logger);

    public static IResult ToHttpResult<T>(this Result<T> result, ILogger logger) =>
        result.IsSuccess ? Results.Ok(result.Value) : MapError(result.Error, logger);

    private static IResult MapError(Error error, ILogger logger)
    {
        var statusCode = error.Code switch
        {
            _ when error.Code.StartsWith("Validation.", StringComparison.Ordinal) =>
                StatusCodes.Status400BadRequest,
            "Permission.Denied" or "Tenant.Mismatch" => StatusCodes.Status403Forbidden,
            _ when error.Code.EndsWith(".NotFound", StringComparison.Ordinal) =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError,
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "Unmapped error code {ErrorCode} returned 500: {ErrorMessage}", error.Code, error.Message);
            return Results.Problem(
                detail: "An unexpected error occurred.", statusCode: statusCode, title: error.Code);
        }

        return Results.Problem(detail: error.Message, statusCode: statusCode, title: error.Code);
    }
}
