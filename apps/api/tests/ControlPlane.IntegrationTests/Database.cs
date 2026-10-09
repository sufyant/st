using ControlPlane.Api;
using ControlPlane.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ControlPlane.IntegrationTests.Database))]

namespace ControlPlane.IntegrationTests;

// One PostgreSQL 18 server for the test assembly, set up the way a deployment is: the bootstrap script creates the
// roles, then the module's migrations run as the owner. Tests connect as the application role. The identity
// provider and the invitation email are systems we do not own, so they are fakes.
public sealed class Database : IAsyncLifetime
{
    public const string AcceptUrl = "https://app.test/invitations/accept";

    private const string Password = "test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _services = null!;

    public IServiceProvider Services => _services;

    public FakeIdentityProvider Identity { get; } = new();

    public FakeInvitationSender Sender { get; } = new();

    public string ConnectionStringFor(string role, string? database = null) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = role,
            Password = Password,
            Database = database ?? MainDatabase,
        }.ConnectionString;

    private string MainDatabase => new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RunScriptAsync("bootstrap.sql", "-v", $"owner_password={Password}", "-v", $"application_password={Password}");

        _services = BuildServices();
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

    // The module as the host composes it, against this database; a test replaces services it needs to control, such as time.
    public ServiceProvider BuildServices(
        Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? settings = null,
        bool realIdentityProvider = false,
        string? database = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Invitations:AcceptUrl"] = AcceptUrl,
            })
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddTenancy(_ => ConnectionStringFor(DatabaseRoles.Application, database))
            .AddControlPlaneModule();
        if (!realIdentityProvider)
        {
            services.Replace(ServiceDescriptor.Singleton<IIdentityProvider>(Identity));
        }

        services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(Sender));
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public Task RunScriptAsync(string script, params string[] variables) => RunScriptInAsync(MainDatabase, script, variables);

    // A database with the roles but no migrations, for a test that migrates it step by step.
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"empty_{Guid.NewGuid():N}";
        await ExecuteAsSuperuserAsync($"CREATE DATABASE {name}");
        await RunScriptInAsync(name, "bootstrap.sql", "-v", $"owner_password={Password}", "-v", $"application_password={Password}");

        return name;
    }

    // What is really in a table: the container's superuser is bound by no policy, so it sees the rows of every tenant.
    public async Task<T> ScalarAsSuperuserAsync<T>(string sql, string? database = null)
    {
        await using var connection = new NpgsqlConnection(SuperuserConnectionStringFor(database));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public async Task ExecuteAsSuperuserAsync(string sql, string? database = null)
    {
        await using var connection = new NpgsqlConnection(SuperuserConnectionStringFor(database));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private string SuperuserConnectionStringFor(string? database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database ?? MainDatabase }.ConnectionString;

    private async Task RunScriptInAsync(string database, string script, params string[] variables)
    {
        await _container.CopyAsync(await File.ReadAllBytesAsync(script), $"/tmp/{script}");

        var result = await _container.ExecAsync(["psql", "--username", "postgres", "--dbname", database, .. variables, "--file", $"/tmp/{script}"]);

        result.ExitCode.ShouldBe(0, result.Stderr);
    }
}
