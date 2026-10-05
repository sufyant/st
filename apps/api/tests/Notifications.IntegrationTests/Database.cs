using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notifications.Api;
using Notifications.Application.Ports;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Notifications.IntegrationTests.Database))]

namespace Notifications.IntegrationTests;

// One PostgreSQL 18 server for the test assembly (0011), set up the way a deployment is: the bootstrap script creates the roles,
// then the module's migrations run as the owner (0018, 0020). Tests connect as the application role. The channels that reach users
// are systems we do not own, so they are a fake that records what it was given.
public sealed class Database : IAsyncLifetime
{
    private const string Password = "test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = role, Password = Password }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");
        var bootstrap = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!,
            "-v", $"owner_password={Password}", "-v", $"application_password={Password}", "-v", $"reporting_password={Password}",
            "--file", "/tmp/bootstrap.sql",
        ]);
        bootstrap.ExitCode.ShouldBe(0, bootstrap.Stderr);

        await using var services = BuildServices(new FakeChannel(), TimeProvider.System);
        foreach (var migrator in services.GetServices<IModuleMigrator>())
        {
            await migrator.MigrateAsync(services, ConnectionStringFor(DatabaseRoles.Owner), CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    // The module as the host composes it, against this database, with the given channel and time.
    public ServiceProvider BuildServices(FakeChannel channel, TimeProvider time)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IHostEnvironment>(new TestEnvironment(Environments.Development))
            .AddLogging()
            .AddSingleton(time)
            .AddTenancy(_ => ConnectionStringFor(DatabaseRoles.Application))
            .AddNotificationsModule(new ConfigurationBuilder().Build());
        services.RemoveAll<INotificationChannel>();
        services.AddSingleton<INotificationChannel>(channel);

        return services.BuildServiceProvider();
    }
}
