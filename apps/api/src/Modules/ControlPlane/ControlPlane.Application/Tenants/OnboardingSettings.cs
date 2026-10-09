namespace ControlPlane.Application.Tenants;

/// <summary>
/// Configuration of tenant onboarding (section <c>ControlPlane</c>), validated at startup; public because a handler takes it as a
/// parameter.
/// </summary>
public sealed class OnboardingSettings
{
    public const string Section = "ControlPlane";

    private static readonly TimeSpan[] DefaultIdentityProviderRetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    /// <summary>From the start of the onboarding until the owner is registered with the identity provider; then the tenant is cancelled.</summary>
    public TimeSpan RegistrationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// From the tenant's activation until the invitation email is sent; then the onboarding needs attention. Longer than the email
    /// retries Notifications is configured with, so it only fires when a message is lost.
    /// </summary>
    public TimeSpan InvitationEmailTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>The growing pauses before each new try of a call to the identity provider; after the last one the step fails for good.</summary>
    /// <remarks>Unset unless configured, so configured pauses replace the defaults: the binder would add them to an array it finds.</remarks>
    public TimeSpan[]? IdentityProviderRetryDelays { get; set; }

    internal IReadOnlyList<TimeSpan> RetryDelays => IdentityProviderRetryDelays ?? DefaultIdentityProviderRetryDelays;

    internal OnboardingTimeouts Timeouts => new(RegistrationTimeout, InvitationEmailTimeout);

    /// <summary>What is wrong with the settings, or nothing.</summary>
    internal IEnumerable<string> Problems()
    {
        if (RegistrationTimeout <= TimeSpan.Zero || InvitationEmailTimeout <= TimeSpan.Zero)
        {
            yield return $"{Section}:RegistrationTimeout and {Section}:InvitationEmailTimeout must be positive.";
        }

        if (RetryDelays.Count == 0 || RetryDelays[0] <= TimeSpan.Zero || RetryDelays.Zip(RetryDelays.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            yield return $"{Section}:IdentityProviderRetryDelays must be positive pauses that grow.";
        }

        if (RegistrationTimeout <= RetryDelays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay))
        {
            yield return $"{Section}:RegistrationTimeout must be longer than the {Section}:IdentityProviderRetryDelays together.";
        }
    }
}
