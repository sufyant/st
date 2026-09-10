using Api.Http;
using Application.Abstractions;
using Application.Features.Me;

namespace Api.Features.Me;

public sealed record AcceptInvitationRequest(string Token);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").RequireAuthorization();

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

            // Gone is the one status this surface needs that no ErrorKind carries, so it is refined
            // here from the not-found default rather than widening the shared classification.
            return !result.IsSuccess && result.Error.Code == MeErrors.ExpiredCode
                ? Results.StatusCode(StatusCodes.Status410Gone)
                : result.ToOk();
        });

        return endpoints;
    }
}
