using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using ControlPlane.Domain.Users;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.IntegrationTests;

// Writes catalog rows directly, the way the onboarding flow will once it exists.
internal static class Catalog
{
    public static async Task<Tenant> AddTenantAsync(IServiceProvider services, TenantStatus status = TenantStatus.Active, string? slug = null)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), slug ?? Unique.Slug()).Value;
        if (status == TenantStatus.Active)
        {
            tenant.Activate();
        }

        await SaveAsync(services, catalog => catalog.Tenants.Add(tenant));
        return tenant;
    }

    public static async Task<User> AddUserAsync(IServiceProvider services, string? externalId = null)
    {
        var user = new User(Guid.CreateVersion7(), externalId ?? Unique.ExternalId());
        await SaveAsync(services, catalog => catalog.Users.Add(user));
        return user;
    }

    public static Task AddMemberAsync(IServiceProvider services, Tenant tenant, User user, Role? role = null) =>
        SaveAsync(services, catalog => catalog.Memberships.Add(new Membership(tenant.Id, user.Id, (role ?? BuiltInRoles.Member).Id)));

    public static async Task<User> AddMemberAsync(IServiceProvider services, Tenant tenant, Role role)
    {
        var user = await AddUserAsync(services);
        await AddMemberAsync(services, tenant, user, role);
        return user;
    }

    private static async Task SaveAsync(IServiceProvider services, Action<CatalogDbContext> change)
    {
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        change(catalog);
        await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}

// The tests share one database, so each one works with tenants, users and email addresses of its own. The values never matter,
// only that they differ.
internal static class Unique
{
    public static string Slug() => $"tenant-{Guid.NewGuid():N}"[..20];

    public static string ExternalId() => $"user_{Guid.NewGuid():N}";

    public static string Email() => $"{Guid.NewGuid():N}@example.com";
}
