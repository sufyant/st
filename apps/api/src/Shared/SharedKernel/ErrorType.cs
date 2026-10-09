namespace SharedKernel;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>The request is well formed but cannot be carried out as it stands, such as an idempotency key reused for another request.</summary>
    Unprocessable,
}
