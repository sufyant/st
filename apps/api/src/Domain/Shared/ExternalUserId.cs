namespace Domain.Shared;

public sealed record ExternalUserId
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
}
