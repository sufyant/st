using System.Reflection;

namespace Architecture.Tests;

// Wolverine discovers only public handlers, and its FluentValidation middleware only public validators. A non-public one is
// skipped without an error: a handler never receives its messages, and a validator's rules never run.
public class WolverineVisibilityTests
{
    public static TheoryData<string> Modules => [.. Solution.Modules];

    [Theory]
    [MemberData(nameof(Modules))]
    public void DiscoverHandlers_InApplication_ArePublic(string module)
    {
        var hidden = NonPublicTypesIn($"{module}.Application")
            .Where(IsHandlerType)
            .Select(type => type.FullName);

        hidden.ShouldBeEmpty();
    }

    // Wolverine finds handler methods by these names and, like handler types, skips the non-public ones.
    [Theory]
    [MemberData(nameof(Modules))]
    public void DiscoverHandlerMethods_InApplication_ArePublic(string module)
    {
        string[] handlerMethodNames =
            ["Handle", "Handles", "HandleAsync", "HandlesAsync", "Consume", "Consumes", "ConsumeAsync", "ConsumesAsync"];

        var hidden = Solution.Load($"{module}.Application").GetTypes()
            .Where(IsHandlerType)
            .SelectMany(type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => handlerMethodNames.Contains(method.Name))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}");

        hidden.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void DiscoverValidators_InApplication_ArePublic(string module)
    {
        var hidden = NonPublicTypesIn($"{module}.Application")
            .Where(type => type.GetInterfaces().Any(IsValidatorInterface))
            .Select(type => type.FullName);

        hidden.ShouldBeEmpty();
    }

    private static IEnumerable<Type> NonPublicTypesIn(string project) =>
        Solution.Load(project).GetTypes().Where(type => !type.IsPublic && !type.IsNestedPublic);

    // Wolverine's conventional discovery finds handler types by these suffixes.
    private static bool IsHandlerType(Type type) =>
        type.Name.EndsWith("Handler", StringComparison.Ordinal) || type.Name.EndsWith("Consumer", StringComparison.Ordinal);

    // Matched by name so the architecture tests need no FluentValidation reference of their own.
    private static bool IsValidatorInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition().FullName == "FluentValidation.IValidator`1";
}
