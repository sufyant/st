using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using FluentValidation;
using SharedKernel;
using Wolverine;
using Wolverine.Persistence;

namespace ControlPlane.Application.Tenants;

/// <summary>
/// A system admin starts a tenant's onboarding. It runs inside the tenant it creates, whose id the caller chose, so every step
/// of the saga runs in that tenant's transaction and hands the tenant on to the next one.
/// </summary>
public sealed record StartTenantOnboarding(string AdminId, string Name, string Slug, string OwnerEmail);

public sealed record TenantDetails(Guid Id, string Name, string Slug, string Status);

public sealed class StartTenantOnboardingValidator : AbstractValidator<StartTenantOnboarding>
{
    public StartTenantOnboardingValidator()
    {
        // The tenant checks the name and the slug itself; a request without one never reaches it.
        RuleFor(command => command.Name).NotNull();
        RuleFor(command => command.Slug).NotNull();
        RuleFor(command => command.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(Invitation.EmailMaxLength);
    }
}

// Step 1 of the onboarding saga, in one transaction: the tenant as provisioning, its first owner's invitation and the saga's record
// (section 6). The invitation's secret is born here; only its hash is stored, and the accept link that carries it goes on in the
// first step's message. Every rejection returns before the handler changes anything, because Wolverine commits a failed Result too
// (W7); a taken slug is found by the insert itself, which writes nothing then.
public static class StartTenantOnboardingHandler
{
    public static async Task<(Result<TenantDetails>, Insert<TenantOnboarding>?, DeliveryMessage<RegisterOwnerWithIdentityProvider>?, RegistrationTimedOut?)> HandleAsync(
        StartTenantOnboarding command,
        ITenantCatalog catalog,
        InvitationSettings invitations,
        OnboardingSettings onboarding,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = Tenant.Create(catalog.TenantId, command.Name, command.Slug);
        if (!tenant.IsSuccess)
        {
            return (tenant.Error, null, null, null);
        }

        var admin = await catalog.FindUserAsync(command.AdminId, cancellationToken)
            ?? throw new InvalidOperationException("A system admin is always a catalog user.");

        if (!await catalog.TryAddAsync(tenant.Value, cancellationToken))
        {
            return (Error.Conflict("tenant.slug_taken", "Another tenant already has this slug."), null, null, null);
        }

        var now = time.GetUtcNow();
        var secret = InvitationToken.Generate();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(now), tenant.Value.Id, command.OwnerEmail, BuiltInRoles.Owner, admin.Id, now, invitations.Lifetime, secret);
        catalog.Add(invitation);

        var (saga, register, timeout) = TenantOnboarding.Begin(
            tenant.Value.Id,
            invitation.Id,
            invitation.Email,
            invitations.AcceptLink(new InvitationCode(tenant.Value.Id, secret)),
            onboarding.Timeouts);

        // A saga that is only being started gives its messages no saga id of their own; this one needs it, so that its fault finds
        // the saga.
        return (
            new TenantDetails(tenant.Value.Id, tenant.Value.Name, tenant.Value.Slug, tenant.Value.Status.ToString()),
            Storage.Insert(saga),
            register.WithDeliveryOptions(new DeliveryOptions { SagaId = saga.Id.ToString() }),
            timeout);
    }
}
