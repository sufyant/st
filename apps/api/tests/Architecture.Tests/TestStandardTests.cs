using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Architecture.Tests;

// Section 9, the unit test standard, read from every test project the solution lists: the classification trait, the name
// pattern, and no branch or loop in a test. Several cases are a theory with data; a helper outside the test may branch.
public partial class TestStandardTests
{
    private const string Classification = "TestClassification";

    private static readonly Dictionary<string, string> ClassificationBySuffix = new()
    {
        [".UnitTests"] = "Unit",
        [".IntegrationTests"] = "Integration",
        [".Tests"] = "Architecture",
        [".EndToEndTests"] = "EndToEnd",
    };

    public static TheoryData<string> TestProjects => [.. Solution.TestProjects];

    public static TheoryData<string, string> TestProjectsWithTheirClassification =>
    [
        .. Solution.TestProjects.Select(project =>
            (project, ClassificationBySuffix.Single(pair => project.EndsWith(pair.Key, StringComparison.Ordinal)).Value)),
    ];

    [Theory]
    [MemberData(nameof(TestProjectsWithTheirClassification))]
    public void ClassifyTests_TestProject_CarriesOneTraitThatMatchesItsSuffix(string project, string classification)
    {
        var values = Solution.Load(project).GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
            .Where(attribute => (string?)attribute.ConstructorArguments[0].Value == Classification)
            .Select(attribute => (string?)attribute.ConstructorArguments[1].Value);

        values.ShouldBe([classification]);
    }

    [Theory]
    [MemberData(nameof(TestProjects))]
    public void NameTests_TestProject_FollowsOperationScenarioExpectedOutcome(string project)
    {
        var misnamed = TestMethodsOf(project)
            .Where(method => !OperationScenarioExpectedOutcome().IsMatch(method.Name))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}");

        misnamed.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(TestProjects))]
    public void ReadTestBodies_TestProject_FindsNoBranchAndNoLoop(string project)
    {
        var tests = TestMethodsOf(project).Select(method => $"{method.DeclaringType!.Name}.{method.Name}").ToHashSet();
        var declarations = Solution.SourceFilesOf(project)
            .SelectMany(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot().DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(method => tests.Contains(NameOf(method)))
            .ToList();

        var branching = declarations.Where(BranchesOrLoops).Select(NameOf);
        var unread = tests.Except(declarations.Select(NameOf));

        branching.ShouldBeEmpty();
        unread.ShouldBeEmpty();
    }

    // Every method xunit runs as a test: [Fact], and [Theory], which derives from it.
    private static IEnumerable<MethodInfo> TestMethodsOf(string project) =>
        Solution.Load(project).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true));

    // The conditional operator, ?? and ??= choose between two values as an if does.
    private static bool BranchesOrLoops(MethodDeclarationSyntax method) =>
        method.DescendantNodes().Any(node => node is IfStatementSyntax
            or SwitchStatementSyntax
            or SwitchExpressionSyntax
            or ConditionalExpressionSyntax
            or ForStatementSyntax
            or CommonForEachStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax
            || node.IsKind(SyntaxKind.CoalesceExpression)
            || node.IsKind(SyntaxKind.CoalesceAssignmentExpression));

    private static string NameOf(MethodDeclarationSyntax method) =>
        $"{method.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.Text}.{method.Identifier.Text}";

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*$")]
    private static partial Regex OperationScenarioExpectedOutcome();
}
