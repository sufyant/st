using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace ControlPlane.Infrastructure;

/// <summary>
/// The one way to reach catalog rows that belong to a tenant. The catalog has no row level security, so this access point,
/// bound to the active tenant, is what keeps one tenant's rows from another: it reads only the active tenant's rows and adds
/// only rows of the active tenant (0021).
/// </summary>
internal sealed class TenantCatalog(CatalogDbContext catalog, TenantContext tenant) : ITenantCatalog
{
    public IQueryable<Membership> Memberships
    {
        get
        {
            var tenantId = ActiveTenant;
            return catalog.Memberships.Where(membership => membership.TenantId == tenantId);
        }
    }

    // Built-in roles belong to no tenant and every tenant has them.
    public IQueryable<Role> Roles
    {
        get
        {
            var tenantId = ActiveTenant;
            return catalog.Roles.Where(role => role.TenantId == tenantId || role.TenantId == null);
        }
    }

    public IQueryable<Invitation> Invitations
    {
        get
        {
            var tenantId = ActiveTenant;
            return catalog.Invitations.Where(invitation => invitation.TenantId == tenantId);
        }
    }

    Guid ITenantCatalog.TenantId => ActiveTenant;

    private Guid ActiveTenant =>
        tenant.TenantId ?? throw new InvalidOperationException("Tenant-owned catalog rows are reached only inside a tenant.");

    // Runs inside the tenant transaction (0016), so the lock holds until it commits or rolls back.
    Task ITenantCatalog.LockAsync(CancellationToken cancellationToken) =>
        catalog.Database.ExecuteSqlAsync($"SELECT 1 FROM catalog.tenants WHERE id = {ActiveTenant} FOR UPDATE", cancellationToken);

    Task<string> ITenantCatalog.FindSlugAsync(CancellationToken cancellationToken)
    {
        var tenantId = ActiveTenant;
        return catalog.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).SingleAsync(cancellationToken);
    }

    Task<Role?> ITenantCatalog.FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Roles.SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    Task<bool> ITenantCatalog.IsRoleNameTakenAsync(string name, Guid? exceptRoleId, CancellationToken cancellationToken) =>
        Roles.AnyAsync(role => role.Name == name && role.Id != exceptRoleId, cancellationToken);

    async Task<bool> ITenantCatalog.IsRoleInUseAsync(Guid roleId, CancellationToken cancellationToken) =>
        await Memberships.AnyAsync(membership => membership.RoleId == roleId, cancellationToken)
        || await Invitations.AnyAsync(invitation => invitation.RoleId == roleId && invitation.Status == InvitationStatus.Pending, cancellationToken);

    Task<Membership?> ITenantCatalog.FindMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        Memberships.SingleOrDefaultAsync(membership => membership.UserId == userId, cancellationToken);

    Task<Membership?> ITenantCatalog.FindMembershipAsync(string externalUserId, CancellationToken cancellationToken) =>
        Memberships.SingleOrDefaultAsync(
            membership => catalog.Users.Any(user => user.Id == membership.UserId && user.ExternalId == externalUserId), cancellationToken);

    Task<int> ITenantCatalog.CountOwnersAsync(CancellationToken cancellationToken) =>
        Memberships.CountAsync(membership => membership.RoleId == BuiltInRoles.Owner.Id, cancellationToken);

    async Task<Invitation?> ITenantCatalog.FindInvitationForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var found = await catalog.Invitations
            .FromSql($"SELECT * FROM catalog.invitations WHERE token_hash = {tokenHash} AND tenant_id = {ActiveTenant} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return found.SingleOrDefault();
    }

    Task<User?> ITenantCatalog.FindUserAsync(string externalUserId, CancellationToken cancellationToken) =>
        catalog.Users.SingleOrDefaultAsync(user => user.ExternalId == externalUserId, cancellationToken);

    public void AddMember(Guid userId, Guid roleId) => catalog.Memberships.Add(new Membership(ActiveTenant, userId, roleId));

    void ITenantCatalog.Add(Role role) => catalog.Roles.Add(OfActiveTenant(role, role.TenantId));

    void ITenantCatalog.Add(Invitation invitation) => catalog.Invitations.Add(OfActiveTenant(invitation, invitation.TenantId));

    void ITenantCatalog.Add(User user) => catalog.Users.Add(user);

    void ITenantCatalog.Remove(Role role) => catalog.Roles.Remove(OfActiveTenant(role, role.TenantId));

    void ITenantCatalog.Remove(Membership membership) => catalog.Memberships.Remove(OfActiveTenant(membership, membership.TenantId));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);

    private T OfActiveTenant<T>(T row, Guid? tenantId) =>
        tenantId == ActiveTenant ? row : throw new InvalidOperationException("Only rows of the active tenant can be written here.");
}
