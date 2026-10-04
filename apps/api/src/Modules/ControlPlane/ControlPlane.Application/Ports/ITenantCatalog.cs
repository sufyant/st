using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;

namespace ControlPlane.Application.Ports;

/// <summary>
/// The one way handlers reach catalog rows that belong to a tenant, bound to the active tenant (0021). The catalog has no row level
/// security, so this access point is what keeps one tenant's rows from another: it reads only the active tenant's rows and adds
/// only rows of the active tenant. Users are not tenant-owned; they are here because accepting an invitation saves a user and its
/// membership together.
/// </summary>
/// <remarks>
/// Public only because Wolverine's generated code passes it to public handlers (0047); its members speak domain types, so they
/// are internal to the module.
/// </remarks>
public interface ITenantCatalog
{
    internal Guid TenantId { get; }

    /// <summary>Locks the tenant's row for the rest of the transaction, so changes to its memberships and roles run one at a time.</summary>
    internal Task LockAsync(CancellationToken cancellationToken);

    internal Task<string> FindSlugAsync(CancellationToken cancellationToken);

    /// <summary>A built-in role, or a custom role of the active tenant.</summary>
    internal Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    internal Task<bool> IsRoleNameTakenAsync(string name, Guid? exceptRoleId, CancellationToken cancellationToken);

    /// <summary>Whether a membership or a pending invitation is assigned the role.</summary>
    internal Task<bool> IsRoleInUseAsync(Guid roleId, CancellationToken cancellationToken);

    internal Task<Membership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken);

    internal Task<Membership?> FindMembershipAsync(string externalUserId, CancellationToken cancellationToken);

    internal Task<int> CountOwnersAsync(CancellationToken cancellationToken);

    /// <summary>The invitation with this token hash, locked for the rest of the transaction so it is accepted only once.</summary>
    internal Task<Invitation?> FindInvitationForUpdateAsync(string tokenHash, CancellationToken cancellationToken);

    internal Task<User?> FindUserAsync(string externalUserId, CancellationToken cancellationToken);

    internal void AddMember(Guid userId, Guid roleId);

    internal void Add(Role role);

    internal void Add(Invitation invitation);

    internal void Add(User user);

    internal void Remove(Role role);

    internal void Remove(Membership membership);

    internal Task SaveChangesAsync(CancellationToken cancellationToken);
}
