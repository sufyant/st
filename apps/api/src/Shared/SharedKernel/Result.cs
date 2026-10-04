namespace SharedKernel;

public class Result
{
    private readonly Error? _error;

    private protected Result(Error? error) => _error = error;

    public bool IsSuccess => _error is null;

    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue _value;

    private Result(TValue value)
        : base(null) => _value = value;

    private Result(Error error)
        : base(error) => _value = default!;

    public TValue Value => IsSuccess ? _value : throw new InvalidOperationException("A failed result has no value.");

    public static implicit operator Result<TValue>(TValue value) => new(value);

    public static implicit operator Result<TValue>(Error error) => new(error);
}
