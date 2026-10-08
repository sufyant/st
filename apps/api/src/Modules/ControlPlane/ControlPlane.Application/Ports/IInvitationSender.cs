namespace ControlPlane.Application.Ports;

/// <summary>Delivers an invitation link to the invited person; we send the email ourselves for a consistent look.</summary>
public interface IInvitationSender
{
    Task SendAsync(string email, Uri link, CancellationToken cancellationToken);
}
