namespace Notifications.Infrastructure.Email;

// Resend's API (section Notifications:Resend), checked on start. The API key comes from the environment only, never from a committed
// file.
internal sealed class ResendOptions
{
    public const string Section = "Notifications:Resend";

    public string? ApiKey { get; set; }

    // The sender, for example "App <no-reply@example.com>", on a domain verified at Resend.
    public string? From { get; set; }

    public Uri ApiUrl { get; set; } = new("https://api.resend.com/");

    // How long a send may take.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
}
