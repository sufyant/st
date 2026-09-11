using Infrastructure.Persistence;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
    .UseNpgsql(
        Required("ControlPlane"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
    .Options;

return args switch
{
    ["migrate", "control-plane", ..] => await MigrateControlPlaneAsync(),
    ["migrate", "tenants", ..] => await MigrateTenantsAsync(configuration["tenant"]),
    _ => Usage()
};

string Required(string name) =>
    configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");

async Task<int> MigrateControlPlaneAsync()
{
    await using var context = new ControlPlaneDbContext(options);
    await context.Database.MigrateAsync();
    Console.WriteLine("control plane: migrated");

    return 0;
}

async Task<int> MigrateTenantsAsync(string? alias)
{
    await using var context = new ControlPlaneDbContext(options);
    var runner = new TenantMigrationRunner(
        context,
        new TenantSchemaMigrator(new TenantDbContextFactory(Required("TenantData"), new AuditInterceptor(TimeProvider.System))));
    var outcomes = await runner.RunAsync(alias, CancellationToken.None);

    foreach (var outcome in outcomes)
    {
        if (outcome.Result == TenantMigrationRunner.Failed)
        {
            Console.Error.WriteLine($"{outcome.Alias}: FAILED - {outcome.Error}");
        }
        else
        {
            Console.WriteLine($"{outcome.Alias}: {outcome.Result}");
        }
    }

    return outcomes.Any(outcome => outcome.Result == TenantMigrationRunner.Failed) ? 1 : 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: migrate control-plane | migrate tenants [--tenant <alias>]");

    return 2;
}
