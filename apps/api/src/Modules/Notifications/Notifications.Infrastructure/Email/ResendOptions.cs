namespace Notifications.Infrastructure.Email;

// Resend's API (section Resend). The API key comes from the environment only, never from a committed file.
internal sealed class ResendOptions
{
    public const string Section = "Resend";

    public string? ApiKey { get; set; }

    // The sender, for example "App <no-reply@example.com>", on a domain verified at Resend.
    public string? From { get; set; }

    public Uri ApiUrl { get; set; } = new("https://api.resend.com/");

    // How long one attempt may take; a send is tried up to four times in all (0041).
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    // The pause before the first retry, doubling with each further one.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
}
