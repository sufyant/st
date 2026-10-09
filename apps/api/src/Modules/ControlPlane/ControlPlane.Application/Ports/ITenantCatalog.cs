using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;
using SharedKernel;

namespace ControlPlane.Application.Ports;

/// <summary>
/// The one way handlers reach catalog rows that belong to a tenant, used inside the active tenant. Row level security keeps one
/// tenant's memberships and invitations from another. Users are not tenant-owned; they are here because accepting an invitation
/// saves a user and its membership together.
/// </summary>
/// <remarks>
/// Public only because Wolverine's generated code passes it to public handlers; its members speak domain types, so they
/// are internal to the module.
/// </remarks>
public interface ITenantCatalog
{
    internal Guid TenantId { get; }

    internal Task<string> FindSlugAsync(CancellationToken cancellationToken);

    /// <summary>The active tenant, locked for the rest of the transaction, so the steps of its onboarding run one at a time.</summary>
    internal Task<Tenant> FindTenantForUpdateAsync(CancellationToken cancellationToken);

    internal Task<Membership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A page of the active tenant's members with their roles, by role (Owner, Admin, Member), then by user id.</summary>
    internal Task<ListPage<(Guid UserId, BuiltInRole Role)>> ListMembersAsync(PageRequest paging, CancellationToken cancellationToken);

    /// <summary>The invitation with this token hash, locked for the rest of the transaction so it is accepted only once.</summary>
    internal Task<Invitation?> FindInvitationForUpdateAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The invitation with this id, locked for the rest of the transaction so it is delivered only once.</summary>
    internal Task<Invitation?> FindInvitationForUpdateAsync(Guid invitationId, CancellationToken cancellationToken);

    internal Task<User?> FindUserAsync(string externalUserId, CancellationToken cancellationToken);

    internal void AddMember(Guid userId, Guid roleId);

    /// <summary>
    /// Adds the active tenant itself, which its onboarding creates, unless another tenant has its slug. The slug's unique index
    /// decides, also between two onboardings at the same moment, and nothing else about the other tenant is revealed.
    /// </summary>
    internal Task<bool> TryAddAsync(Tenant tenant, CancellationToken cancellationToken);

    internal void Add(Invitation invitation);

    internal void Add(User user);

    internal Task SaveChangesAsync(CancellationToken cancellationToken);
}
