namespace Domain.Shared;

public sealed record EmailAddress
{
    private const int MaximumLength = 320;

    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    public static EmailAddress Create(string value) =>
        TryCreate(value, out var email)
            ? email
            : throw new ArgumentException("Email address is not valid.", nameof(value));

    public static bool TryCreate(string? value, out EmailAddress email)
    {
        var normalised = value?.Trim().ToLowerInvariant();
        var atIndex = normalised?.IndexOf('@') ?? -1;

        if (string.IsNullOrWhiteSpace(normalised) ||
            normalised.Length > MaximumLength ||
            atIndex <= 0 ||
            atIndex == normalised.Length - 1 ||
            normalised.LastIndexOf('@') != atIndex)
        {
            email = null!;

            return false;
        }

        email = new EmailAddress(normalised);

        return true;
    }
}
