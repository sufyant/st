using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace ControlPlane.IntegrationTests;

// The first system admin is created by a seed script during setup.
public sealed class SystemAdminTests(Database database)
{
    [Fact]
    public async Task The_seed_script_makes_a_user_a_system_admin()
    {
        var externalId = Unique.ExternalId();

        await SeedAsync(externalId);

        (await FindSystemPermissionsAsync(externalId)).ShouldNotBeNull().ShouldBe(Permissions.SystemPool, ignoreOrder: true);
    }

    [Fact]
    public async Task The_seed_script_grants_an_existing_user()
    {
        var user = await Catalog.AddUserAsync(database.Services);

        await SeedAsync(user.ExternalId);

        (await FindSystemPermissionsAsync(user.ExternalId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Running_the_seed_script_again_changes_nothing()
    {
        var externalId = Unique.ExternalId();
        await SeedAsync(externalId);

        await SeedAsync(externalId);

        (await FindSystemPermissionsAsync(externalId)).ShouldNotBeNull().ShouldBe(Permissions.SystemPool, ignoreOrder: true);
    }

    [Fact]
    public async Task A_user_who_is_not_a_system_admin_has_no_system_permissions()
    {
        var user = await Catalog.AddUserAsync(database.Services);

        var permissions = await FindSystemPermissionsAsync(user.ExternalId);

        permissions.ShouldBeNull();
    }

    private Task SeedAsync(string externalId) => database.RunScriptAsync("seed-system-admin.sql", "-v", $"external_id={externalId}");

    private async Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(string externalId)
    {
        await using var scope = database.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISystemAdminDirectory>()
            .FindSystemPermissionsAsync(externalId, TestContext.Current.CancellationToken);
    }
}
