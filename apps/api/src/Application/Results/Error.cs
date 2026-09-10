namespace Application.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden
}

public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFailures =
        new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, string[]> Failures { get; init; } = NoFailures;

    public static Error Validation(IReadOnlyDictionary<string, string[]> failures) =>
        new(ErrorKind.Validation, "validation.failed", "The request is not valid.")
        {
            Failures = failures
        };

    public static Error NotFound(string code, string message) =>
        new(ErrorKind.NotFound, code, message);

    public static Error Conflict(string code, string message) =>
        new(ErrorKind.Conflict, code, message);

    public static Error Forbidden(string code, string message) =>
        new(ErrorKind.Forbidden, code, message);
}
