using Audit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;
using Wolverine;

[assembly: AssemblyFixture(typeof(Audit.IntegrationTests.Database))]

namespace Audit.IntegrationTests;

// One PostgreSQL 18 server for the test assembly, set up the way a deployment is: the bootstrap script creates the roles,
// then the module's migrations run as the owner. Tests connect as the application role. Wolverine's own test double stands in for
// the message context, which carries the tenant.
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
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");
        var bootstrap = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!,
            "-v", $"owner_password={Password}", "-v", $"application_password={Password}",
            "--file", "/tmp/bootstrap.sql",
        ]);
        bootstrap.ExitCode.ShouldBe(0, bootstrap.Stderr);

        _services = new ServiceCollection()
            .AddTenancy(_ => ConnectionStringFor(DatabaseRoles.Application))
            .AddAuditInfrastructure()
            .AddScoped<IMessageContext>(_ => new TestMessageContext())
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
}
