namespace ControlPlane.Application.Invitations;

/// <summary>Configuration of invitations (section <c>Invitations</c>); public because handlers take it as a parameter.</summary>
public sealed class InvitationSettings
{
    public const string Section = "Invitations";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The client page where a signed-in person accepts an invitation; the invitation code is added as the <c>code</c> parameter.</summary>
    public Uri? AcceptUrl { get; set; }

    // Checked on first use, not on start: the build starts the host to write the OpenAPI document without configuration.
    internal Uri AcceptLink(InvitationCode code)
    {
        var acceptUrl = AcceptUrl ?? throw new InvalidOperationException($"{Section}:AcceptUrl must name the page that accepts invitations.");
        return new UriBuilder(acceptUrl) { Query = $"code={Uri.EscapeDataString(code.ToString())}" }.Uri;
    }
}
