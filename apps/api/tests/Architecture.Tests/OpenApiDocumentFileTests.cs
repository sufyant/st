using System.Diagnostics;

namespace Architecture.Tests;

// Section 7: the build writes the OpenAPI document to openapi/v1.json, and the file is committed, so a change to the API shows in
// the pull request. The build rewrites the file and does not fail, so this test compares what it wrote with what git has.
public sealed class OpenApiDocumentFileTests
{
    private const string DocumentFile = "openapi/v1.json";

    [Fact]
    public async Task BuildOpenApiDocument_CurrentEndpoints_IsTheCommittedFile()
    {
        var (exitCode, difference) = await GitDiffAsync(DocumentFile);

        exitCode.ShouldBe(0, $"The build wrote a different {DocumentFile}. Add it to the commit:{Environment.NewLine}{difference}");
    }

    // The file as the build left it against the file in git's index, which is the committed file until the change is staged.
    private static async Task<(int ExitCode, string Output)> GitDiffAsync(string file)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Solution.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("diff");
        start.ArgumentList.Add("--exit-code");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(file);

        using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var output = git.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = git.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await git.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (git.ExitCode, await output + await error);
    }
}
