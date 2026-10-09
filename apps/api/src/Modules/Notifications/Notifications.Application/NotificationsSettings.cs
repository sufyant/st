namespace Notifications.Application;

// Configuration of the Notifications module (section Notifications), validated at startup by its infrastructure.
internal sealed class NotificationsSettings
{
    public const string Section = "Notifications";

    private static readonly TimeSpan[] DefaultInvitationEmailRetryDelays =
        [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    // The growing pauses before each new try of the invitation email; the email service may be down for a while. Unset unless
    // configured, so configured pauses replace the defaults: the binder would add them to an array it finds.
    public TimeSpan[]? InvitationEmailRetryDelays { get; set; }

    public IReadOnlyList<TimeSpan> RetryDelays => InvitationEmailRetryDelays ?? DefaultInvitationEmailRetryDelays;

    public bool IsValid =>
        RetryDelays.Count > 0 && RetryDelays[0] > TimeSpan.Zero && RetryDelays.Zip(RetryDelays.Skip(1)).All(pair => pair.Second > pair.First);
}
