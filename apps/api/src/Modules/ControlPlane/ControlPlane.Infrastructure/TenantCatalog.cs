using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
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

    Guid ITenantCatalog.TenantId => ActiveTenant;

    private Guid ActiveTenant =>
        tenant.TenantId ?? throw new InvalidOperationException("Tenant-owned catalog rows are reached only inside a tenant.");

    Task<string> ITenantCatalog.FindSlugAsync(CancellationToken cancellationToken)
    {
        var tenantId = ActiveTenant;
        return catalog.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).SingleAsync(cancellationToken);
    }

    async Task<Tenant> ITenantCatalog.FindTenantForUpdateAsync(CancellationToken cancellationToken)
    {
        var found = await catalog.Tenants
            .FromSql($"SELECT * FROM catalog.tenants WHERE id = {ActiveTenant} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return found.Single();
    }

    Task<Membership?> ITenantCatalog.FindMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        Memberships.SingleOrDefaultAsync(membership => membership.UserId == userId, cancellationToken);

    async Task<Invitation?> ITenantCatalog.FindInvitationForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var found = await catalog.Invitations
            .FromSql($"SELECT * FROM catalog.invitations WHERE token_hash = {tokenHash} AND tenant_id = {ActiveTenant} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return found.SingleOrDefault();
    }

    async Task<Invitation?> ITenantCatalog.FindInvitationForUpdateAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var found = await catalog.Invitations
            .FromSql($"SELECT * FROM catalog.invitations WHERE id = {invitationId} AND tenant_id = {ActiveTenant} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return found.SingleOrDefault();
    }

    Task<User?> ITenantCatalog.FindUserAsync(string externalUserId, CancellationToken cancellationToken) =>
        catalog.Users.SingleOrDefaultAsync(user => user.ExternalId == externalUserId, cancellationToken);

    public void AddMember(Guid userId, Guid roleId) => catalog.Memberships.Add(new Membership(ActiveTenant, userId, roleId));

    // An insert of a slug that another onboarding has inserted but not yet committed waits for it, and is skipped if it commits.
    async Task<bool> ITenantCatalog.TryAddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var added = OfActiveTenant(tenant, tenant.Id);
        var inserted = await catalog.Database.ExecuteSqlAsync(
            $"INSERT INTO catalog.tenants (id, slug, status) VALUES ({added.Id}, {added.Slug}, {added.Status.ToString()}) ON CONFLICT (slug) DO NOTHING",
            cancellationToken);

        return inserted == 1;
    }

    void ITenantCatalog.Add(Invitation invitation) => catalog.Invitations.Add(OfActiveTenant(invitation, invitation.TenantId));

    void ITenantCatalog.Add(User user) => catalog.Users.Add(user);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);

    private T OfActiveTenant<T>(T row, Guid? tenantId) =>
        tenantId == ActiveTenant ? row : throw new InvalidOperationException("Only rows of the active tenant can be written here.");
}
