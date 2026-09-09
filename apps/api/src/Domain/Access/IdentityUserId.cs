namespace Domain.Access;

public sealed class IdentityUserId : IEquatable<IdentityUserId>
{
    public string Value { get; }

    private IdentityUserId(string value)
    {
        Value = value;
    }

    public static IdentityUserId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity user ID cannot be blank.", nameof(value));
        }

        return new IdentityUserId(value);
    }

    public bool Equals(IdentityUserId? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is IdentityUserId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
