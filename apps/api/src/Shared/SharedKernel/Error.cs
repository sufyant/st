using System.Diagnostics.CodeAnalysis;

namespace SharedKernel;

/// <summary>An expected failure: a business outcome the caller handles, not an exception.</summary>
[SuppressMessage("Naming", "CA1716", Justification = "Error is a keyword only in Visual Basic, which no consumer of the API uses.")]
public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error Unprocessable(string code, string description) => new(code, description, ErrorType.Unprocessable);
}
