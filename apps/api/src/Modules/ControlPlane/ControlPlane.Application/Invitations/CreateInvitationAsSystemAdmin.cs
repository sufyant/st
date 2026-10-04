using ControlPlane.Application.Ports;
using FluentValidation;
using SharedKernel;

namespace ControlPlane.Application.Invitations;

/// <summary>
/// A system admin, inside the tenant's context, invites someone to it with any role, for example its first owner (0029, 0031).
/// The admin route group has already checked the admin's system permissions.
/// </summary>
public sealed record CreateInvitationAsSystemAdmin(string AdminId, string Email, Guid RoleId);

public sealed class CreateInvitationAsSystemAdminValidator : AbstractValidator<CreateInvitationAsSystemAdmin>
{
    public CreateInvitationAsSystemAdminValidator() =>
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(Domain.Invitations.Invitation.EmailMaxLength);
}

public static class CreateInvitationAsSystemAdminHandler
{
    public static async Task<Result<InvitationDetails>> HandleAsync(
        CreateInvitationAsSystemAdmin command,
        ITenantCatalog catalog,
        IIdentityProvider identity,
        IInvitationSender sender,
        InvitationSettings settings,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var admin = await catalog.FindUserAsync(command.AdminId, cancellationToken)
            ?? throw new InvalidOperationException("A system admin is always a catalog user.");

        if (await catalog.FindRoleAsync(command.RoleId, cancellationToken) is not { } role)
        {
            return Errors.RoleNotFound;
        }

        return await InvitationIssuer.IssueAsync(
            command.Email, role, admin.Id, new(catalog, identity, sender, settings, time), cancellationToken);
    }
}
