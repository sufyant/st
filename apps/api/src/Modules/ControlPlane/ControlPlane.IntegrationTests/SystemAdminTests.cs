using ControlPlane.Contracts;
using ControlPlane.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.IntegrationTests;

// Section 6: the first system admin comes from configuration. While the staff list is empty, the person who reaches the system door
// with a second factor and a verified email address equal to ControlPlane:FirstSystemAdminEmail becomes a system admin. Each test
// has a database of its own, so its staff list starts empty.
public sealed class SystemAdminTests(Database database)
{
    private const string FirstAdminEmail = "First.Admin@Example.com";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VisitTheSystemDoor_TheConfiguredPersonOnTheirFirstVisit_BecomesTheFirstSystemAdmin()
    {
        await using var services = await StaffWithoutAdminsAsync();
        var person = Person("first.admin@example.com");

        var permissions = await VisitAsync(services, person);

        permissions.ShouldNotBeNull().ShouldBe(PermissionPools.SystemPool, ignoreOrder: true);
        (await StaffAsync(services)).ShouldBe(1);
    }

    [Fact]
    public async Task VisitTheSystemDoor_APersonWithAnotherEmail_DoesNotBecomeASystemAdmin()
    {
        await using var services = await StaffWithoutAdminsAsync();

        var permissions = await VisitAsync(services, Person("someone.else@example.com"));

        permissions.ShouldBeNull();
        (await StaffAsync(services)).ShouldBe(0);
    }

    [Fact]
    public async Task VisitTheSystemDoor_TheConfiguredPersonWithoutASecondFactor_DoesNotBecomeASystemAdmin()
    {
        await using var services = await StaffWithoutAdminsAsync();

        var permissions = await VisitAsync(services, Person("first.admin@example.com"), secondFactorVerified: false);

        permissions.ShouldBeNull();
        (await StaffAsync(services)).ShouldBe(0);
    }

    // Only the provider's verified addresses count: one the person typed, or one waiting for verification, proves nothing.
    [Fact]
    public async Task VisitTheSystemDoor_TheConfiguredEmailIsNotVerified_DoesNotBecomeASystemAdmin()
    {
        await using var services = await StaffWithoutAdminsAsync();

        var permissions = await VisitAsync(services, Person());

        permissions.ShouldBeNull();
        (await StaffAsync(services)).ShouldBe(0);
    }

    [Fact]
    public async Task VisitTheSystemDoor_AfterTheFirstSystemAdminExists_TheSettingMakesNobodyElseOne()
    {
        await using var services = await StaffWithoutAdminsAsync();
        await VisitAsync(services, Person("first.admin@example.com"));
        var second = Person("first.admin@example.com");

        var permissions = await VisitAsync(services, second);

        permissions.ShouldBeNull();
        (await StaffAsync(services)).ShouldBe(1);
    }

    // Two pods may let the same person in at the same moment; the staff list is checked in the statement that writes it.
    [Fact]
    public async Task VisitTheSystemDoor_TwoFirstVisitsAtOnce_CreateOneSystemAdmin()
    {
        await using var services = await StaffWithoutAdminsAsync();
        var person = Person("first.admin@example.com");

        var visits = await Task.WhenAll(VisitAsync(services, person), VisitAsync(services, person));

        visits.ShouldAllBe(permissions => permissions != null);
        (await StaffAsync(services)).ShouldBe(1);
    }

    [Fact]
    public async Task VisitTheSystemDoor_AUserTheCatalogKnows_BecomesTheFirstSystemAdminWithTheSameId()
    {
        await using var services = await StaffWithoutAdminsAsync();
        var user = await Catalog.AddUserAsync(services);
        database.Identity.AddAccount(user.ExternalId, "first.admin@example.com");

        await VisitAsync(services, user.ExternalId);

        (await database.ScalarAsSuperuserAsync<Guid>("SELECT user_id FROM catalog.system_admins", _database)).ShouldBe(user.Id);
    }

    private string _database = null!;

    private async Task<ServiceProvider> StaffWithoutAdminsAsync()
    {
        _database = await database.CreateMigratedDatabaseAsync();
        return database.BuildServices(database: _database, settings: new() { ["ControlPlane:FirstSystemAdminEmail"] = FirstAdminEmail });
    }

    // A person the identity provider knows, with the verified email addresses given.
    private string Person(params string[] verifiedEmails)
    {
        var externalId = Unique.ExternalId();
        database.Identity.AddAccount(externalId, verifiedEmails);
        return externalId;
    }

    private static async Task<IReadOnlySet<string>?> VisitAsync(IServiceProvider services, string externalId, bool secondFactorVerified = true)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISystemAdminDirectory>()
            .FindSystemPermissionsAsync(externalId, secondFactorVerified, Cancellation);
    }

    private Task<long> StaffAsync(IServiceProvider services) =>
        database.ScalarAsSuperuserAsync<long>("SELECT count(*) FROM catalog.system_admins", _database);
}
