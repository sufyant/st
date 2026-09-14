using Api.Http;
using Application.Abstractions;
using Application.Features.Me;

namespace Api.Features.Me;

public sealed record AcceptInvitationRequest(string Token);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ApiRoutes.Version1}")
            .RequireAuthorization()
            .WithTags("MeEndpoints");

        group.MapGet("/memberships", async (IMediator mediator, CancellationToken cancellationToken) =>
            (await mediator.SendAsync(new ListMyMembershipsQuery(), cancellationToken)).ToOk());

        group.MapGet("/invitations", async (IMediator mediator, CancellationToken cancellationToken) =>
            (await mediator.SendAsync(new ListMyInvitationsQuery(), cancellationToken)).ToOk());

        group.MapPost("/invitations/accept", async (
            AcceptInvitationRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync(
                new AcceptInvitationCommand(request.Token),
                cancellationToken);

            // Gone and Service Unavailable are the statuses this surface needs that no ErrorKind
            // carries, so they are refined here rather than widening the shared classification.
            if (!result.IsSuccess)
            {
                if (result.Error.Code == MeErrors.ExpiredCode)
                {
                    return Results.StatusCode(StatusCodes.Status410Gone);
                }

                if (result.Error.Code == MeErrors.CredentialUnavailableCode)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            }

            return result.ToOk();
        });

        return endpoints;
    }
}
