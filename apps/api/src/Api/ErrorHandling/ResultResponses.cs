using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;

namespace Api.ErrorHandling;

// Endpoints return the Result of their command, and ResultEndpointFilter maps it (0032). The response type inferred from the
// handler is therefore Result itself, which no client ever receives. This convention replaces it with what the filter sends, so
// the OpenAPI document describes the real responses (0036).
internal static class ResultResponses
{
    private static readonly int[] FailureStatuses =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
    ];

    public static TBuilder DescribeResults<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Finally(endpoint =>
        {
            var inferred = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
                .FirstOrDefault(response => response.Type is { } type && typeof(Result).IsAssignableFrom(type));
            if (inferred is null)
            {
                return;
            }

            endpoint.Metadata.Remove(inferred);
            endpoint.Metadata.Add(inferred.Type!.IsGenericType
                ? new ProducesResponseTypeMetadata(StatusCodes.Status200OK, inferred.Type.GetGenericArguments()[0], ["application/json"])
                : new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void)));

            foreach (var status in FailureStatuses)
            {
                var problem = status == StatusCodes.Status400BadRequest ? typeof(HttpValidationProblemDetails) : typeof(ProblemDetails);
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(status, problem, ["application/problem+json"]));
            }
        });

        return builder;
    }
}
