namespace ControlPlane.Infrastructure.Clerk;

// Clerk's Backend API (section Clerk). The secret key comes from the environment only, never from a committed file.
internal sealed class ClerkOptions
{
    public const string Section = "Clerk";

    public string? SecretKey { get; set; }

    public Uri BackendApiUrl { get; set; } = new("https://api.clerk.com/v1/");

    // How long one attempt may take; a call is tried up to four times in all (0041).
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    // The pause before the first retry, doubling with each further one.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
