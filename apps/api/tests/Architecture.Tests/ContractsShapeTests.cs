using System.Reflection;

namespace Architecture.Tests;

// Section 2: a module's Contracts project holds interfaces, DTOs and events, and no domain type. It is all another module sees.
public class ContractsShapeTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static TheoryData<string> Modules => [.. Solution.Modules];

    [Theory]
    [MemberData(nameof(Modules))]
    public void PublishContracts_EveryPublicType_IsAnInterfaceRecordEnumOrConstantsAndShowsNoDomainType(string module)
    {
        var published = Solution.Load($"{module}.Contracts").GetExportedTypes();

        var otherShapes = published
            .Where(type => !type.IsInterface && !type.IsEnum && !IsRecord(type) && !IsStaticClassOfConstants(type))
            .Select(type => type.FullName);
        var domainTypesShown = published
            .SelectMany(type => PublicSignatureOf(type).Select(shown => (type, shown)))
            .Where(pair => Solution.LayerOfEveryModule("Domain").Contains(pair.shown.Assembly.GetName().Name))
            .Select(pair => $"{pair.type.FullName}: {pair.shown.FullName}");

        otherShapes.ShouldBeEmpty();
        domainTypesShown.ShouldBeEmpty();
    }

    // The compiler gives a record class a clone method no source can name, and a record struct its member printer.
    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$") is not null || (type.IsValueType && type.GetMethod("PrintMembers", Declared) is not null);

    private static bool IsStaticClassOfConstants(Type type) =>
        type is { IsClass: true, IsAbstract: true, IsSealed: true }
        && type.GetMembers(Declared).All(member => member is FieldInfo { IsLiteral: true });

    // Every type a caller meets through the public members: base types, interfaces, fields, properties, events, and the
    // parameters and return types of methods and constructors, with their generic arguments and element types.
    private static IEnumerable<Type> PublicSignatureOf(Type type) =>
        new[] { type.BaseType }.OfType<Type>()
            .Concat(type.GetInterfaces())
            .Concat(type.GetFields().Select(field => field.FieldType))
            .Concat(type.GetProperties().Select(property => property.PropertyType))
            .Concat(type.GetEvents().Select(@event => @event.EventHandlerType!))
            .Concat(type.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Concat(type.GetConstructors().SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
            .SelectMany(Unwrap);

    private static IEnumerable<Type> Unwrap(Type type) =>
        type.HasElementType ? Unwrap(type.GetElementType()!)
        : type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type.GetGenericTypeDefinition())
        : [type];
}
