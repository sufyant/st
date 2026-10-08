using System.Diagnostics;
using Api.Persistence;

namespace Api.IntegrationTests;

// Runs the migration step the way a deployment does, as `dotnet Api.dll migrate`. The host is built into the test's output
// folder, so the command runs from there.
internal static class MigrateCommand
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string migrationsConnectionString)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("Api.dll");
        start.ArgumentList.Add(MigrationStep.Command);
        start.Environment["ConnectionStrings__Migrations"] = migrationsConnectionString;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The migration step did not start.");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await output + await error);
    }
}
