namespace Application.Results;

public sealed class Result<T>
{
    private readonly T value;
    private readonly Error? error;

    private Result(T value)
    {
        this.value = value;
    }

    private Result(Error error)
    {
        this.error = error;
        value = default!;
    }

    public bool IsSuccess => error is null;

    public T Value => IsSuccess
        ? value
        : throw new InvalidOperationException("A failed result has no value.");

    public Error Error => error
                          ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}

public static class Result
{
    public static Result<Unit> Success() => Result<Unit>.Success(Unit.Value);
}
