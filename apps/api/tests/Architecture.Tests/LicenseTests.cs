using System.Text.Json;
using System.Xml.Linq;

namespace Architecture.Tests;

// Fixed rule 4: no commercially licensed package. docs/licenses.md lists every package of every project in the solution, direct and
// transitive, with the license the package declares. A package that is not listed, or whose license is not permitted, fails here
// before it reaches a release.
public sealed class LicenseTests
{
    private const string LicensesFile = "docs/licenses.md";

    // Permissive licenses. Any other, commercial and source-available ones above all, needs a decision first.
    private static readonly string[] Permitted = ["MIT", "Apache-2.0", "BSD-3-Clause", "PostgreSQL"];

    [Fact]
    public void ListPackages_EveryProjectOfTheSolution_HasExactlyTheRowsOfItsPackages()
    {
        var used = Solution.SolutionProjects.SelectMany(PackagesOf).Distinct();

        var listed = Listed().Select(row => (row.Package, row.Version));

        listed.ShouldBe(used, ignoreOrder: true);
    }

    [Fact]
    public void ReadLicenses_EveryListedPackage_IsTheLicenseItsPackageDeclares()
    {
        var declared = Solution.SolutionProjects.SelectMany(DeclaredLicensesOf).Distinct();

        var listed = Listed();

        listed.ShouldBe(declared, ignoreOrder: true);
    }

    [Fact]
    public void ReadLicenses_EveryListedPackage_IsPermittedAndNotNamedByFixedRule4()
    {
        var listed = Listed();

        listed.Where(row => !Permitted.Contains(row.License)).ShouldBeEmpty();
        listed.Where(row => IsNamedByFixedRule4(row.Package, row.Version)).ShouldBeEmpty();
    }

    // The rows of the table in the file, after its header and separator: package, version and license.
    private static List<(string Package, string Version, string License)> Listed() =>
    [
        .. File.ReadLines(Path.Combine(Solution.Root, LicensesFile))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Skip(2)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => (cells[1], cells[2], cells[3])),
    ];

    private static IEnumerable<(string Package, string Version)> PackagesOf(string project)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(Solution.AssetsFileOf(project)));

        return
        [
            .. assets.RootElement.GetProperty("libraries").EnumerateObject()
                .Where(library => library.Value.GetProperty("type").GetString() == "package")
                .Select(library => library.Name.Split('/'))
                .Select(nameAndVersion => (nameAndVersion[0], nameAndVersion[1])),
        ];
    }

    // The license expression in each package's own manifest, in the folder NuGet restored it to.
    private static IEnumerable<(string Package, string Version, string License)> DeclaredLicensesOf(string project)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(Solution.AssetsFileOf(project)));
        var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();

        return
        [
            .. PackagesOf(project).Select(package => (package.Package, package.Version, LicenseIn(ManifestOf(package.Package, package.Version, folders)))),
        ];
    }

    private static string ManifestOf(string package, string version, IEnumerable<string> folders) =>
        folders
            .Select(folder => Path.Combine(folder, package.ToLowerInvariant(), version.ToLowerInvariant(), $"{package.ToLowerInvariant()}.nuspec"))
            .First(File.Exists);

    // A package without a license expression (a license file or a URL only) reads as such, which no permitted license matches.
    private static string LicenseIn(string manifest) =>
        XDocument.Load(manifest).Descendants().FirstOrDefault(element => element.Name.LocalName == "license" && (string?)element.Attribute("type") == "expression")?.Value.Trim()
        ?? $"no license expression in {Path.GetFileName(manifest)}";

    // MediatR, AutoMapper, FluentAssertions, and MassTransit from version 9.
    private static bool IsNamedByFixedRule4(string package, string version)
    {
        string[] anyVersion = ["MediatR", "AutoMapper", "FluentAssertions"];
        if (anyVersion.Any(name => package == name || package.StartsWith($"{name}.", StringComparison.Ordinal)))
        {
            return true;
        }

        return (package == "MassTransit" || package.StartsWith("MassTransit.", StringComparison.Ordinal))
            && int.Parse(version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture) >= 9;
    }
}
