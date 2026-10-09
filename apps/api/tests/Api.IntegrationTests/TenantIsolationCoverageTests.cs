namespace Api.IntegrationTests;

// How isolation behaves is proven in Tenancy.IntegrationTests; this proves every tenant table gets it.
public sealed class TenantIsolationCoverageTests(Database database)
{
    [Fact]
    public async Task CheckIsolation_EveryTenantEntityOfTheApplication_IsIsolated()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var gaps = await new TenantIsolationCheck(database).GapsAsync(api.Services);

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckIsolation_EveryModuleDatabase_IsCovered()
    {
        await using var host = await PipelineHost.StartAsync();

        var entities = TenantIsolationCheck.TenantEntities(host.Services).Select(entity => entity.ClrType);
        var gaps = await new TenantIsolationCheck(database).GapsAsync(host.Services);

        entities.ShouldContain(typeof(Probe));
        gaps.ShouldBeEmpty();
    }

    // R6: a table is either on the explicit list of tables without a tenant or isolated; a new table that is neither fails here.
    // R11: a policy is either the tenant isolation policy of a tenant table or on the explicit list of extra policies.
    [Fact]
    public async Task CheckTables_EveryTableAndPolicyInTheDatabase_IsOnTheListOrIsolated()
    {
        var gaps = await new DatabaseTables(database).GapsAsync();

        gaps.ShouldBeEmpty();
    }

    // Permissive policies combine with OR, so a stray one widens access. A database of its own keeps the stray policy away from
    // the other tests.
    [Fact]
    public async Task CheckPolicies_APolicyThatIsNotOnTheList_IsAGap()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        var tables = new DatabaseTables(database, name);
        await database.ScalarAsSuperuserAsync<object>("CREATE POLICY stray ON catalog.invitations FOR SELECT USING (true)", name);

        var withTheStrayPolicy = await tables.GapsAsync();
        await database.ScalarAsSuperuserAsync<object>("DROP POLICY stray ON catalog.invitations", name);
        var withoutIt = await tables.GapsAsync();

        withTheStrayPolicy.ShouldBe(["catalog.invitations: policy stray (SELECT) is neither tenant isolation on a tenant table nor listed"]);
        withoutIt.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadTableOwners_ApplicationRole_OwnsNoTable()
    {
        var owned = await database.ScalarAsync<long>("SELECT count(*) FROM pg_tables WHERE tableowner = 'api_application'");

        owned.ShouldBe(0);
    }
}
