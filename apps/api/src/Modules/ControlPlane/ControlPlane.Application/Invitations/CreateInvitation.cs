using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using FluentValidation;
using SharedKernel;

namespace ControlPlane.Application.Invitations;

/// <summary>A member invites someone to the active tenant with a role no greater than their own (0029, 0030).</summary>
public sealed record CreateInvitation(string ActorId, string Email, Guid RoleId);

public sealed class CreateInvitationValidator : AbstractValidator<CreateInvitation>
{
    public CreateInvitationValidator() =>
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(Domain.Invitations.Invitation.EmailMaxLength);
}

public static class CreateInvitationHandler
{
    public static async Task<Result<InvitationDetails>> HandleAsync(
        CreateInvitation command,
        ITenantCatalog catalog,
        IIdentityProvider identity,
        IInvitationSender sender,
        InvitationSettings settings,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var actor = await Actor.FindAsync(catalog, command.ActorId, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        if (await catalog.FindRoleAsync(command.RoleId, cancellationToken) is not { } role)
        {
            return Errors.RoleNotFound;
        }

        if (RoleGrant.Allows(actor.Value.Permissions, role.Permissions) is { IsSuccess: false } refused)
        {
            return refused.Error;
        }

        return await InvitationIssuer.IssueAsync(
            command.Email, role, actor.Value.UserId, new(catalog, identity, sender, settings, time), cancellationToken);
    }
}
