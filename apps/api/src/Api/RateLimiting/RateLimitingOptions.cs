namespace Api.RateLimiting;

internal sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}
