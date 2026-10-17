using System.Reflection;
using System.Text.Json.Nodes;
using Api.ErrorHandling;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;
using SharedKernel;

namespace Api.Hosting;

// N11: the OpenAPI document is the contract every client is generated from. These transformers add what a generated client needs and
// the endpoints do not state: an operation id for each endpoint, the parameters a route group puts in a path, the values of a list's
// sort and filters, the values of an enum with a converter of its own, and the members of the one error schema with the enums of the
// error codes. They change the document only; the JSON on the wire stays as it is.
internal static class OpenApiContract
{
    public static OpenApiOptions DescribeContract(this OpenApiOptions options) =>
        options.AddOperationTransformer(NameOperation)
            .AddOperationTransformer(DeclarePathParameters)
            .AddOperationTransformer(DescribeListParameters)
            .AddSchemaTransformer(DescribeEnumValues)
            .AddDocumentTransformer(DescribeProblemDetails);

    // N7: an endpoint is a method with the action's name, so the method's name is the operation id.
    private static Task NameOperation(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.OperationId ??= context.Description.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault()?.Name;
        return Task.CompletedTask;
    }

    // OpenAPI 3.1, "Path Templating": each parameter in a path is a declared path parameter. A route group's parameter, such as the
    // tenant's {tenantId} (T1), reaches no endpoint method, so the document would leave it out. The description's path has lost the
    // route constraints; the endpoint's route diagnostics metadata keeps the route as it was mapped, so {tenantId:guid} is a uuid.
    private static Task DeclarePathParameters(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var route = RoutePatternFactory.Parse(context.Description.ActionDescriptor.EndpointMetadata.OfType<IRouteDiagnosticsMetadata>().Single().Route);
        foreach (var parameter in route.Parameters.Where(parameter => operation.Parameters?.Any(declared => declared.Name == parameter.Name) != true))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = ParameterLocation.Path,
                Required = true,
                Schema = parameter.ParameterPolicies.Any(policy => policy.Content == "guid")
                    ? new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
                    : new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        return Task.CompletedTask;
    }

    // K5: the sort and each filter of a list are enums, so a generated client knows their values. Their parameter types bind any text,
    // which ListRequest and ListFilter check, so the document would call them strings; the values are the JSON names the check accepts.
    private static Task DescribeListParameters(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var description in context.Description.ParameterDescriptions)
        {
            var type = Nullable.GetUnderlyingType(description.Type) ?? description.Type;
            var values = type.IsGenericType ? ListValuesOf(type.GetGenericTypeDefinition(), type.GetGenericArguments()[0]) : null;
            if (values is not null && operation.Parameters?.SingleOrDefault(parameter => parameter.Name == description.Name) is OpenApiParameter parameter)
            {
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. values.Select(value => JsonValue.Create(value))] };
            }
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<string>? ListValuesOf(Type parameter, Type values)
    {
        var describe = parameter == typeof(ListSort<>) ? typeof(ListSort) : parameter == typeof(ListFilter<>) ? typeof(ListFilter) : null;
        return (IReadOnlyList<string>?)describe?.GetMethod(nameof(ListSort.ValuesOf))!.MakeGenericMethod(values).Invoke(null, null);
    }

    // The generator knows the values of the default enum converter only. An enum with a converter of its own, such as Language and
    // MemberRole, which read an exact name only (ExactNameEnumConverter), would so have a schema without values, and a nullable field of
    // it no schema at all. So the schema states the JSON names, the names the converter reads, and a nullable field refers to the schema
    // and may be null. OpenApiContractTests fails for a field that holds a language and does not refer to the schema Language.
    private static async Task DescribeEnumValues(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsEnum && schema.Enum is null)
        {
            var values = (IReadOnlyList<string>)typeof(ListFilter).GetMethod(nameof(ListFilter.ValuesOf))!
                .MakeGenericMethod(type).Invoke(null, null)!;
            schema.Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value))];
        }
        else if (Nullable.GetUnderlyingType(type) is { IsEnum: true } underlying && schema is { Type: null, Enum: null, OneOf: null })
        {
            schema.OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Null },
                await context.GetOrCreateSchemaAsync(underlying, cancellationToken: cancellationToken),
            ];
        }
    }

    // K5: every error answer is Problem Details (ResultResponses) with a code. The host adds four members to its schema: the code, one
    // of the one enum of error codes; the field codes of a request that failed validation, an enum too; the trace id; and the tenants an
    // error is about, which only some errors name. The enums are
    // schemas of their own, so a generated client has a type for each and can check that a screen has a text for every code.
    private static Task DescribeProblemDetails(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas?.TryGetValue(nameof(ProblemDetails), out var found) != true || found is not OpenApiSchema problem)
        {
            return Task.CompletedTask;
        }

        document.Components.Schemas[nameof(ErrorCode)] = EnumSchema(
            DeclaredErrorCodes, "The error's code, area.reason. A client shows its own text for it, never the English detail.");
        document.Components.Schemas[nameof(FieldErrorCode)] = EnumSchema(Problems.FieldCodes, "Why one field of a request failed validation.");

        problem.Properties ??= new Dictionary<string, IOpenApiSchema>();
        problem.Properties[Problems.CodeMember] = new OpenApiSchemaReference(nameof(ErrorCode), document);
        problem.Properties[Problems.ErrorsMember] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchemaReference(nameof(FieldErrorCode), document) },
            Description = "The field codes for each field of a request that failed validation, by the field's JSON name.",
        };
        problem.Properties[Problems.TraceIdMember] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "The trace id of the request, for the logs.",
        };
        problem.Properties[Problems.TenantIdsMember] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            Description = "The tenants the error is about, when a client needs them to act, such as the tenants a user is the last Owner of.",
        };
        problem.Required ??= new HashSet<string>();
        problem.Required.Add(Problems.CodeMember);
        return Task.CompletedTask;
    }

    // K5: each owner declares its codes in a static class whose name ends with ErrorCodes: SharedKernel, the host, and each module in its
    // Domain project. The document joins them into one enum. Only an assembly that references SharedKernel can declare a code, so the
    // search starts at the host and follows only those references. ErrorCodeTests checks that it finds every declared code.
    private static IReadOnlyList<string> DeclaredErrorCodes { get; } = FindDeclaredErrorCodes();

    private static List<string> FindDeclaredErrorCodes()
    {
        var kernel = typeof(ErrorCode).Assembly.GetName().Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(OpenApiContract).Assembly]);
        var codes = new List<string>();
        while (pending.TryDequeue(out var assembly))
        {
            if (!seen.Add(assembly.GetName().Name!))
            {
                continue;
            }

            codes.AddRange(assembly.GetTypes()
                .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("ErrorCodes", StringComparison.Ordinal))
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(field => field.FieldType == typeof(ErrorCode))
                .Select(field => ((ErrorCode)field.GetValue(null)!).Value));
            foreach (var referenced in assembly.GetReferencedAssemblies().Where(name => !seen.Contains(name.Name!)).Select(Assembly.Load))
            {
                if (referenced.GetName().Name == kernel || referenced.GetReferencedAssemblies().Any(name => name.Name == kernel))
                {
                    pending.Enqueue(referenced);
                }
            }
        }

        return [.. codes.Distinct().Order(StringComparer.Ordinal)];
    }

    private static OpenApiSchema EnumSchema(IReadOnlyList<string> values, string description) =>
        new() { Type = JsonSchemaType.String, Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value))], Description = description };
}
