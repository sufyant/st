using Application.Results;

namespace Api.Http;

public static class ResultExtensions
{
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();

    public static IResult ToNoContent<T>(this Result<T> result) =>
        result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess
            ? Results.Created(location(result.Value), result.Value)
            : result.Error.ToProblem();

    public static IResult ToProblem(this Error error) => error.Kind switch
    {
        ErrorKind.Validation => Results.ValidationProblem(
            error.Failures.ToDictionary(entry => entry.Key, entry => entry.Value),
            title: "One or more validation errors occurred."),
        ErrorKind.NotFound => Problem(error, StatusCodes.Status404NotFound, "Not Found"),
        ErrorKind.Conflict => Problem(error, StatusCodes.Status409Conflict, "Conflict"),
        ErrorKind.Forbidden => Problem(error, StatusCodes.Status403Forbidden, "Forbidden"),
        _ => throw new InvalidOperationException($"'{error.Kind}' has no HTTP mapping.")
    };

    private static IResult Problem(Error error, int statusCode, string title) => Results.Problem(
        detail: error.Message,
        statusCode: statusCode,
        title: title,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
