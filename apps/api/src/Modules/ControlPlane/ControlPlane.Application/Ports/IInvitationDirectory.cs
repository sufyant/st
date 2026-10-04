namespace ControlPlane.Application.Ports;

/// <summary>
/// Finds the tenant of an invitation from its token before any tenant is known, so that accepting it can run under that tenant
/// (0021, 0029). It reveals nothing but the tenant id.
/// </summary>
public interface IInvitationDirectory
{
    Task<Guid?> FindTenantAsync(string token, CancellationToken cancellationToken);
}
