namespace Api.IntegrationTests;

// How isolation behaves is proven in Tenancy.IntegrationTests; this proves every tenant table gets it.
public sealed class TenantIsolationCoverageTests(Database database)
{
    [Fact]
    public async Task Every_tenant_entity_of_the_application_is_isolated()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var gaps = await new TenantIsolationCheck(database).GapsAsync(api.Services);

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_check_covers_the_tenant_entities_of_every_module_database()
    {
        await using var host = await PipelineHost.StartAsync();

        var entities = TenantIsolationCheck.TenantEntities(host.Services).Select(entity => entity.ClrType);
        var gaps = await new TenantIsolationCheck(database).GapsAsync(host.Services);

        entities.ShouldContain(typeof(Probe));
        gaps.ShouldBeEmpty();
    }

    // R6: a table is either on the explicit list of tables without a tenant or isolated; a new table that is neither fails here.
    [Fact]
    public async Task CheckTables_EveryTableInTheDatabase_IsOnTheListOrIsolated()
    {
        var gaps = await new DatabaseTables(database).GapsAsync();

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_application_role_owns_no_table()
    {
        var owned = await database.ScalarAsync<long>("SELECT count(*) FROM pg_tables WHERE tableowner = 'api_application'");

        owned.ShouldBe(0);
    }
}
