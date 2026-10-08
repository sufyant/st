using System.Diagnostics;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Onboarding.EndToEndTests.Database))]

namespace Onboarding.EndToEndTests;

// One PostgreSQL 18 server (0011), set up the way a deployment is: the bootstrap script creates the roles (0018), the migration step
// runs as `dotnet Api.dll migrate` (0020), and the seed script makes the first system admin (0031).
public sealed class Database : IAsyncLifetime
{
    public const string SystemAdmin = "user_e2e_system_admin";

    private const string Password = "test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public string ApplicationConnectionString => ConnectionStringFor(DatabaseRoles.Application);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RunScriptAsync("bootstrap.sql", "-v", $"owner_password={Password}", "-v", $"application_password={Password}");
        await MigrateAsync();
        await RunScriptAsync("seed-system-admin.sql", "-v", $"external_id={SystemAdmin}");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = role, Password = Password }.ConnectionString;

    private async Task RunScriptAsync(string script, params string[] variables)
    {
        await _container.CopyAsync(await File.ReadAllBytesAsync(script), $"/tmp/{script}");

        var result = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!,
            .. variables, "--file", $"/tmp/{script}",
        ]);

        result.ExitCode.ShouldBe(0, result.Stderr);
    }

    // The host is built into this project's output folder.
    private async Task MigrateAsync()
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("Api.dll");
        start.ArgumentList.Add("migrate");
        start.Environment["ConnectionStrings__Migrations"] = ConnectionStringFor(DatabaseRoles.Owner);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The migration step did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.ShouldBe(0, await output + await error);
    }
}
