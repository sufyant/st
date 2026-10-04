using ControlPlane.Api;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ControlPlane.IntegrationTests.Database))]

namespace ControlPlane.IntegrationTests;

// One PostgreSQL 18 server for the test assembly (0011), set up the way a deployment is: the bootstrap script creates the
// roles, then the module's migrations run as the owner (0018, 0020). Tests connect as the application role.
public sealed class Database : IAsyncLifetime
{
    private const string Password = "test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _services = null!;

    public IServiceProvider Services => _services;

    public string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = role, Password = Password }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RunBootstrapScriptAsync();

        _services = new ServiceCollection()
            .AddTenancy(_ => ConnectionStringFor(DatabaseRoles.Application))
            .AddControlPlaneModule()
            .BuildServiceProvider();

        foreach (var migrator in _services.GetServices<IModuleMigrator>())
        {
            await migrator.MigrateAsync(_services, ConnectionStringFor(DatabaseRoles.Owner), CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task RunBootstrapScriptAsync()
    {
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");

        var result = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!,
            "-v", $"owner_password={Password}", "-v", $"application_password={Password}", "-v", $"reporting_password={Password}",
            "--file", "/tmp/bootstrap.sql",
        ]);

        result.ExitCode.ShouldBe(0, result.Stderr);
    }
}
