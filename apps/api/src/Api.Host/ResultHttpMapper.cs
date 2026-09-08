using Api.Application;

namespace Api.Host;

public static class ResultHttpMapper
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : MapError(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : MapError(result.Error);

    private static IResult MapError(Error error)
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

        return Results.Problem(detail: error.Message, statusCode: statusCode, title: error.Code);
    }
}
