using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

// The other catalog reader that is not bound to a tenant: an invitation is accepted outside any tenant, and its token leads to
// the tenant it is then accepted in. It reveals only the tenant id.
internal sealed class InvitationDirectory(CatalogDbContext catalog) : IInvitationDirectory
{
    public Task<Guid?> FindTenantAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = InvitationToken.Hash(token);
        return catalog.Invitations
            .Where(invitation => invitation.TokenHash == tokenHash)
            .Select(invitation => (Guid?)invitation.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
