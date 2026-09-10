using Application.Abstractions;
using Application.Results;
using Domain.Access;
using FluentValidation;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Invitations;

public sealed record CreatedInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt,
    string Token);

[RequiresPermission(TenantPermissions.InvitationsManage)]
public sealed record CreateInvitationCommand(string Email, string RoleCode) : ICommand<CreatedInvitation>;

public sealed class CreateInvitationValidator : AbstractValidator<CreateInvitationCommand>
{
    public CreateInvitationValidator()
    {
        RuleFor(command => command.Email)
            .Must(email => EmailAddress.TryCreate(email, out _))
            .WithMessage("'{PropertyValue}' is not a valid email address.");
    }
}

public sealed class CreateInvitationHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantDbContext tenantDbContext,
    TenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CreateInvitationCommand, CreatedInvitation>
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<Result<CreatedInvitation>> HandleAsync(
        CreateInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email);
        var roleExists = await tenantDbContext.Roles
            .AnyAsync(role => role.Code == request.RoleCode, cancellationToken);

        if (!roleExists)
        {
            return Result<CreatedInvitation>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.RoleCode)] = [$"Role '{request.RoleCode}' does not exist."]
            }));
        }

        var alreadyInvited = await controlPlaneDbContext.Invitations.AnyAsync(
            invitation => invitation.TenantId == tenantContext.TenantId
                          && invitation.Email == email
                          && invitation.Status == InvitationStatus.Pending,
            cancellationToken);

        if (alreadyInvited)
        {
            return Result<CreatedInvitation>.Failure(Error.Conflict(
                "invitation.duplicate",
                $"'{email.Value}' already has a pending invitation."));
        }

        var token = InvitationTokens.Create();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(),
            tenantContext.TenantId,
            email,
            request.RoleCode,
            InvitationTokens.Hash(token),
            currentUser.Id,
            timeProvider.GetUtcNow(),
            Lifetime);
        controlPlaneDbContext.Invitations.Add(invitation);

        // The plain token is returned once and never stored; only its digest is persisted.
        return Result<CreatedInvitation>.Success(new CreatedInvitation(
            invitation.Id,
            invitation.Email.Value,
            invitation.RoleCode,
            invitation.ExpiresAt,
            token));
    }
}
