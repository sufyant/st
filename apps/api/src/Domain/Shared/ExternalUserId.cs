namespace Domain.Shared;

public sealed class ExternalUserId : IEquatable<ExternalUserId>
{
    public string Value { get; }

    private ExternalUserId(string value)
    {
        Value = value;
    }

    public static ExternalUserId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("External user ID cannot be blank.", nameof(value));
        }

        return new ExternalUserId(value);
    }

    public bool Equals(ExternalUserId? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is ExternalUserId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
