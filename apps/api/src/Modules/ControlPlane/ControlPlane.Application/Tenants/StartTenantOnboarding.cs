using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Tenants;
using FluentValidation;
using SharedKernel;

namespace ControlPlane.Application.Tenants;

/// <summary>
/// A system admin starts a tenant's onboarding. It runs inside the tenant it creates, whose id the caller chose, so every step
/// of the saga runs in that tenant's transaction and hands the tenant on to the next one.
/// </summary>
public sealed record StartTenantOnboarding(string AdminId, string Slug, string OwnerEmail);

public sealed record TenantDetails(Guid Id, string Slug, string Status);

public sealed class StartTenantOnboardingValidator : AbstractValidator<StartTenantOnboarding>
{
    public StartTenantOnboardingValidator() =>
        RuleFor(command => command.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(Invitation.EmailMaxLength);
}

public static class StartTenantOnboardingHandler
{
    public static async Task<(Result<TenantDetails>, CreateFirstOwnerInvitation?)> HandleAsync(
        StartTenantOnboarding command,
        ITenantCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = Tenant.Create(catalog.TenantId, command.Slug);
        if (!tenant.IsSuccess)
        {
            return (tenant.Error, null);
        }

        var admin = await catalog.FindUserAsync(command.AdminId, cancellationToken)
            ?? throw new InvalidOperationException("A system admin is always a catalog user.");

        if (!await catalog.TryAddAsync(tenant.Value, cancellationToken))
        {
            return (Error.Conflict("tenant.slug_taken", "Another tenant already has this slug."), null);
        }

        return (
            new TenantDetails(tenant.Value.Id, tenant.Value.Slug, tenant.Value.Status.ToString()),
            new CreateFirstOwnerInvitation(Guid.CreateVersion7(time.GetUtcNow()), command.OwnerEmail, admin.Id));
    }
}
